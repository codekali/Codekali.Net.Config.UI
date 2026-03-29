// ── State ─────────────────────────────────────────────────────────────────
let files = [];
let currentFile = null;
let currentEntries = [];
let expandedPaths = new Set();
let revealedPaths = new Set();
let activeEditorPath = null;
let autoSaveOnBlur = true;
let searchQuery = '';
let editorMode = 'tree';
let currentView = 'editor';
const revealedValues = {};
const _token = new URLSearchParams(window.location.search).get('token') ?? '';

// ── Monaco state ──────────────────────────────────────────────────────────
let _monacoInstance = null;
let _monacoModels = {};
let _monacoFallback = false;
let _monacoInitPromise = null;

// ── Boot ──────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', async () => {
   if (window.READONLY) {
      document.getElementById('readonly-badge').classList.remove('hidden');
      document.getElementById('add-btn').disabled = true;
      document.getElementById('backup-btn').disabled = true;
   }
   await loadFileList();
});

// ── API ───────────────────────────────────────────────────────────────────
async function api(method, path, body) {
   const url = window.CONFIG_UI_API_BASE + path;
   const opts = { method, headers: { 'Content-Type': 'application/json' } };
   if (body !== undefined) opts.body = JSON.stringify(body);
   if (_token) {
   const sep = path.includes('?') ? '&' : '?';
      opts.headers['X-Config-Token'] = _token;
      url += `${sep}token=${encodeURIComponent(_token)}`;
   }
   const res = await fetch(url, opts);
   return res.json();
}

// ── Files ─────────────────────────────────────────────────────────────────
async function loadFileList() {
   const r = await api('GET', '/files');
   if (!r.success) { toast('Failed to load files: ' + r.error, 'error'); return; }
   files = r.data;
   renderFileList();
   // Fix #1 — repopulate only the diff selects on reload; the swap selects are
   // populated separately so that a running swap operation is not disturbed.
   populateDiffSelects();
   // Only populate swap selects if they have never been populated (first load).
   // After executeSwap() completes we call refreshSwapKeysOnly() instead.
   if (!_swapSelectsPopulated) {
      populateSwapSelects();
      _swapSelectsPopulated = true;
   }
   updateWelcomeStats();
}

// Tracks whether the swap <select> elements have been populated at least once.
let _swapSelectsPopulated = false;

function updateWelcomeStats() {
   const count = files.length;
   const envs = new Set(files.map(f => f.environment)).size;
   const sorted = [...files].sort((a, b) => new Date(b.lastModified) - new Date(a.lastModified));
   const modText = sorted.length
      ? `${sorted[0].fileName} · ${timeAgo(sorted[0].lastModified)}`
      : '–';

   // #editor-no-file stats
   _setText('ws-files', count);
   _setText('ws-envs', envs);
   _setText('ws-modified', modText);
   // #view-guide stats
   _setText('gs-files', count);
   _setText('gs-envs', envs);
   _setText('gs-modified', modText);
}

function _setText(id, val) {
   const el = document.getElementById(id);
   if (el) el.textContent = val;
}

function timeAgo(isoString) {
   const diff = Math.floor((Date.now() - new Date(isoString).getTime()) / 1000);
   if (diff < 60) return `${diff}s ago`;
   if (diff < 3600) return `${Math.floor(diff / 60)}m ago`;
   if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`;
   return `${Math.floor(diff / 86400)}d ago`;
}

function toggleAutoSave(btn) {
   autoSaveOnBlur = !autoSaveOnBlur;
   btn.setAttribute('aria-checked', autoSaveOnBlur);
   btn.style.background = autoSaveOnBlur ? 'var(--accent)' : 'var(--border)';
   btn.querySelector('span').style.left = autoSaveOnBlur ? '16px' : '2px';
}

function showSaveStatus() {
   if (!autoSaveOnBlur) return;
   const status = document.getElementById('save-status');
   status.textContent = 'Saving...';
   status.style.opacity = '1';
   setTimeout(() => {
      status.textContent = 'Saved';
      setTimeout(() => { status.style.opacity = '0'; }, 2000);
   }, 600);
}

function renderFileList() {
   const el = document.getElementById('file-list');
   if (!files.length) {
      el.innerHTML = '<div class="text-muted" style="padding:12px;font-size:12px">No appsettings files found.</div>';
      return;
   }
   el.innerHTML = files.map(f => {
      const env = f.environment;
      const cls = env === 'Base' ? 'base' : env === 'Development' ? 'dev' : env === 'Production' ? 'prod' : env === 'Staging' ? 'stg' : '';
      const active = currentFile && currentFile.fileName === f.fileName;
      return `<div class="file-item ${active ? 'active' : ''}" onclick="selectFile('${escAttr(f.fileName)}')">
              <span class="file-env-pill ${cls}">${env.slice(0, 4)}</span>
              <span class="file-name" title="${escAttr(f.fileName)}">${escHtml(f.fileName)}</span>
            </div>`;
   }).join('');
}

async function selectFile(fileName) {
   currentFile = files.find(f => f.fileName === fileName);
   expandedPaths.clear();
   revealedPaths.clear();
   Object.keys(revealedValues).forEach(k => delete revealedValues[k]);
   activeEditorPath = null;
   searchQuery = '';
   document.getElementById('search-input').value = '';

   renderFileList();

   // Fix #2 — toolbar rows are revealed HERE (only after a file is selected)
   // and are hidden again by hideToolbar() if the user deselects / clears.
   document.getElementById('editor-toolbar-row1').classList.remove('hidden');
   document.getElementById('editor-toolbar-row2').classList.remove('hidden');
   document.getElementById('toolbar-title').innerHTML =
      `<strong>${escHtml(fileName)}</strong> <span class="text-muted monospace" style="font-size:11px">— ${escHtml(currentFile.environment)}</span>`;

   // Hide the "no file" landing state and show the content area
   document.getElementById('editor-no-file').classList.add('hidden');

   setView('editor');
   await loadEntries();
}

// Hides toolbar rows and restores the "no file" landing state.
// Called when there is no selected file and the editor view is active.
function _showNoFileState() {
   document.getElementById('editor-toolbar-row1').classList.add('hidden');
   document.getElementById('editor-toolbar-row2').classList.add('hidden');
   document.getElementById('editor-no-file').classList.remove('hidden');
   document.getElementById('tree-view').classList.add('hidden');
   document.getElementById('raw-view').classList.add('hidden');
   document.getElementById('editor-loading').style.display = 'none';
}

// ── Load entries ──────────────────────────────────────────────────────────
async function loadEntries() {
   showEditorLoading(true);
   if (editorMode === 'tree') {
      const r = await api('GET', `/files/${enc(currentFile.fileName)}/entries`);
      if (!r.success) { toast(r.error, 'error'); showEditorLoading(false); return; }
      currentEntries = r.data;
      renderTree();
   } else {
      const r = await api('GET', `/files/${enc(currentFile.fileName)}/raw`);
      if (!r.success) { toast(r.error, 'error'); showEditorLoading(false); return; }
      await setRawEditorContent(r.data);
      showEditorLoading(false);
      document.getElementById('raw-view').classList.remove('hidden');
   }
}

async function refreshFile() {
   if (currentFile) { activeEditorPath = null; await loadEntries(); }
}

// ── Tree rendering ────────────────────────────────────────────────────────
function renderTree() {
   const container = document.getElementById('tree-view');
   const entries = searchQuery ? filterEntries(currentEntries, searchQuery, '') : currentEntries;
   container.innerHTML = entries.length
      ? renderNodes(entries, 0, '')
      : '<div class="text-muted" style="padding:16px;font-size:13px">No keys match your search.</div>';
   showEditorLoading(false);
   container.classList.remove('hidden');
   if (activeEditorPath) {
      const slot = document.getElementById('slot-' + pathToId(activeEditorPath));
      if (slot) openInlineEditor(slot, activeEditorPath);
   }
}

function renderNodes(nodes, depth, parentPath) {
   return nodes.map(n => renderNode(n, depth, parentPath)).join('');
}

function renderNode(node, depth, parentPath) {
   const fullPath = parentPath ? `${parentPath}:${node.key}` : node.key;
   const slotId = 'slot-' + pathToId(fullPath);
   const indent = depth * 20;
   const isObj = node.valueType === 'object';
   const isExpanded = expandedPaths.has(fullPath);
   const isRevealed = revealedPaths.has(fullPath);

   let valueHtml = '';
   if (isObj) {
      valueHtml = `<span class="tree-type-badge" style="margin-left:4px">{${node.children?.length ?? 0}}</span>`;
   } else {
      let rawVal = node.rawValue;
      let displayVal;
      if (node.isMasked && !isRevealed) {
         displayVal = '••••••••';
      } else if (node.isMasked && isRevealed) {
         displayVal = revealedValues[fullPath.trim()] ?? '';
      } else if (rawVal === null || rawVal === undefined || rawVal === 'null') {
         displayVal = 'null';
      } else if (node.valueType === 'string') {
         displayVal = rawVal.startsWith('"') && rawVal.endsWith('"') ? rawVal.slice(1, -1) : rawVal;
      } else {
         displayVal = rawVal;
      }
      const cls = (node.isMasked && !isRevealed) ? 'masked' : node.valueType;
      valueHtml = `<span class="tree-colon">:</span><span class="tree-value ${cls}">${escHtml(displayVal)}</span>`;
      if (node.isMasked) {
         valueHtml += ` <button class="btn btn-ghost btn-sm" onclick="toggleReveal('${escAttr(fullPath)}',event)"
                style="padding:1px 5px;font-size:11px">${isRevealed ? '🙈 hide' : '👁 reveal'}</button>`;
      }
   }

   const toggleHtml = isObj
      ? `<span class="tree-toggle" onclick="toggleExpand('${escAttr(fullPath)}',event)">${isExpanded ? '▾' : '▸'}</span>`
      : `<span class="tree-toggle"></span>`;
   const rowClick = isObj ? `onclick="toggleExpand('${escAttr(fullPath)}',event)"` : '';
   let actions = '';
   if (!window.READONLY) {
      actions = `<span class="row-actions"><button class="btn btn-danger btn-sm" onclick="confirmDelete('${escAttr(fullPath)}',event)">✕</button></span>`;
   }
   let childHtml = '';
   if (isObj && isExpanded && node.children?.length) {
      childHtml = `<div>${renderNodes(node.children, depth + 1, fullPath)}</div>`;
   }
   const dblClick = window.READONLY ? '' : `ondblclick="startEdit('${escAttr(fullPath)}',${isObj},event)"`;
   return `<div class="tree-node">
            <div class="tree-row" style="padding-left:${8 + indent}px;cursor:${window.READONLY ? 'default' : 'pointer'}" ${rowClick} ${dblClick}
              title="${window.READONLY ? '' : 'Double-click to edit'}">
              ${toggleHtml}
              <span class="tree-key">${escHtml(node.key)}</span>
              ${valueHtml}
              ${actions}
            </div>
            <div id="${slotId}" class="hidden"></div>
            ${childHtml}
          </div>`;
}

function pathToId(path) { return path.replace(/[^a-zA-Z0-9]/g, '_'); }

function toggleExpand(fullPath, e) {
   e.stopPropagation();
   if (expandedPaths.has(fullPath)) expandedPaths.delete(fullPath); else expandedPaths.add(fullPath);
   renderTree();
}

async function toggleReveal(fullPath, e) {
   e.stopPropagation();
   if (revealedPaths.has(fullPath)) { revealedPaths.delete(fullPath); renderTree(); return; }
   if (!revealedValues[fullPath.trim()]) {
      const r = await api('GET', `/files/${enc(currentFile.fileName)}/value?key=${enc(fullPath)}`);
      if (!r.success) { toast('Could not reveal value: ' + r.error, 'error'); return; }
      revealedValues[fullPath.trim()] = r.data;
   }
   revealedPaths.add(fullPath.trim());
   renderTree();
}

function onSearch() {
   searchQuery = document.getElementById('search-input').value.trim().toLowerCase();
   if (currentFile) renderTree();
}

function filterEntries(entries, q, parentPath) {
   const result = [];
   for (const e of entries) {
      const fullPath = parentPath ? `${parentPath}:${e.key}` : e.key;
      const keyMatch = e.key.toLowerCase().includes(q);
      const valMatch = (e.rawValue ?? '').toLowerCase().includes(q);
      if (keyMatch || valMatch) {
         result.push(e);
      } else if (e.children?.length) {
         const fc = filterEntries(e.children, q, fullPath);
         if (fc.length) { expandedPaths.add(fullPath); result.push({ ...e, children: fc }); }
      }
   }
   return result;
}

// ── Inline editor ─────────────────────────────────────────────────────────
async function startEdit(fullPath, isObj, e) {
   e.stopPropagation();
   if (activeEditorPath === fullPath) return;
   const entry = findEntry(currentEntries, fullPath);
   if (entry?.isMasked && !revealedValues[fullPath.trim()]) {
      const r = await api('GET', `/files/${enc(currentFile.fileName)}/value?key=${enc(fullPath)}`);
      if (!r.success) { toast('Could not load value for editing: ' + r.error, 'error'); return; }
      revealedValues[fullPath.trim()] = r.data;
   }
   activeEditorPath = fullPath;
   if (isObj) expandedPaths.add(fullPath);
   renderTree();
}

function openInlineEditor(slot, fullPath) {
   const entry = findEntry(currentEntries, fullPath);
   const isObj = entry?.valueType === 'object';
   let prefill = '';
   if (isObj && entry?.children) {
      prefill = entriesToJson(entry.children);
   } else if (entry?.isMasked) {
      prefill = revealedValues[fullPath.trim()] ?? '';
   } else {
      const raw = entry?.rawValue ?? '';
      prefill = (entry?.valueType === 'string' && raw.startsWith('"') && raw.endsWith('"'))
         ? raw.slice(1, -1) : raw;
   }
   const inputId = 'ei-' + pathToId(fullPath);
   const indentPx = 8 + (fullPath.split(':').length - 1) * 20;
   slot.innerHTML = `
      <div class="inline-editor" style="margin:2px 8px 4px ${indentPx}px">
        <span class="tree-key monospace" style="font-size:11px;flex-shrink:0;margin-right:4px;color:var(--accent2)">${escHtml(fullPath)}</span>
        <input class="inline-input" id="${inputId}" value="${escAttr(prefill)}"
          onkeydown="handleEditKey(event,'${escAttr(fullPath)}')" />
        <button class="btn btn-primary btn-sm" onclick="submitEdit('${escAttr(fullPath)}')">Save</button>
        <button class="btn btn-ghost btn-sm"   onclick="cancelEdit()">✕</button>
      </div>`;
   slot.classList.remove('hidden');
   const inp = document.getElementById(inputId);
   if (inp) {
      inp.focus(); inp.select();
      inp.addEventListener('blur', () => {
         setTimeout(() => {
            const active = document.activeElement;
            const editor = slot.querySelector('.inline-editor');
            if (editor && !editor.contains(active)) {
               if (autoSaveOnBlur) { showSaveStatus(); submitEdit(fullPath); }
               else cancelEdit();
            }
         }, 120);
      });
   }
}

function handleEditKey(e, fullPath) {
   if (e.key === 'Enter') { showSaveStatus(); submitEdit(fullPath); }
   if (e.key === 'Escape') cancelEdit();
}

async function submitEdit(fullPath) {
   const inputId = 'ei-' + pathToId(fullPath);
   const inp = document.getElementById(inputId);
   if (!inp) return;
   const entry = findEntry(currentEntries, fullPath);
   const isObj = entry?.valueType === 'object';
   let jsonValue = inp.value;
   if (!isObj && entry?.valueType === 'string') {
      try { JSON.parse(jsonValue); } catch { jsonValue = JSON.stringify(jsonValue); }
   }
   const r = await api('PUT', `/files/${enc(currentFile.fileName)}/entries`, { keyPath: fullPath, jsonValue });
   if (r.success) { toast('Saved ✓', 'success'); activeEditorPath = null; await loadEntries(); }
   else toast(r.error, 'error');
}

function cancelEdit() { activeEditorPath = null; renderTree(); }

function findEntry(entries, fullPath) {
   const parts = fullPath.split(':');
   let current = entries;
   let found = null;
   for (let i = 0; i < parts.length; i++) {
      found = (current ?? []).find(e => e.key === parts[i]);
      if (!found) return null;
      current = found.children;
   }
   return found;
}

function entriesToJson(children) {
   if (!children) return '{}';
   const obj = {};
   for (const c of children) {
      if (c.valueType === 'object') obj[c.key] = JSON.parse(entriesToJson(c.children));
      else { try { obj[c.key] = JSON.parse(c.rawValue ?? 'null'); } catch { obj[c.key] = c.rawValue; } }
   }
   return JSON.stringify(obj, null, 2);
}

// ── Add key ───────────────────────────────────────────────────────────────
function showAddForm() { document.getElementById('add-form').classList.remove('hidden'); }
function hideAddForm() { document.getElementById('add-form').classList.add('hidden'); }

async function submitAdd() {
   const key = document.getElementById('add-key').value.trim();
   const val = document.getElementById('add-value').value.trim();
   if (!key) { toast('Key path is required', 'warn'); return; }
   if (!currentFile) { toast('Select a file first', 'warn'); return; }
   const r = await api('POST', `/files/${enc(currentFile.fileName)}/entries`, { keyPath: key, jsonValue: val });
   if (r.success) {
      toast('Key added ✓', 'success');
      document.getElementById('add-key').value = '';
      document.getElementById('add-value').value = '';
      hideAddForm();
      await loadEntries();
   } else toast(r.error, 'error');
}

// ── Delete ────────────────────────────────────────────────────────────────
function confirmDelete(fullPath, e) {
   e.stopPropagation();
   showModal(
      'Delete key?',
      `Delete <strong class="monospace">${escHtml(fullPath)}</strong> from <strong>${escHtml(currentFile.fileName)}</strong>?`,
      async () => {
         const r = await api('DELETE', `/files/${enc(currentFile.fileName)}/entries/${enc(fullPath)}`);
         if (r.success) { toast('Key deleted ✓', 'success'); activeEditorPath = null; await loadEntries(); }
         else toast(r.error, 'error');
      }
   );
}

// ── Backup ────────────────────────────────────────────────────────────────
async function createBackup() {
   if (!currentFile) { toast('Select a file first', 'warn'); return; }
   const r = await api('POST', `/files/${enc(currentFile.fileName)}/backup`);
   if (r.success) toast('Backup created ✓', 'success');
   else toast(r.error, 'error');
}

// ── Monaco Editor ─────────────────────────────────────────────────────────
async function initMonaco() {
   if (_monacoInitPromise) return _monacoInitPromise;
   _monacoInitPromise = (async () => {
      let monaco;
      try { monaco = await window.monacoReady; }
      catch { _monacoFallback = true; showMonacoFallback(); updateMonacoStatusBadge(false); return; }

      monaco.editor.defineTheme('configui-dark', {
         base: 'vs-dark', inherit: true,
         rules: [
            { token: 'string.key.json', foreground: '7dd3fc' },
            { token: 'string.value.json', foreground: '4ade80' },
            { token: 'number', foreground: 'fb923c' },
            { token: 'keyword.json', foreground: 'c084fc' },
         ],
         colors: {
            'editor.background': '#161b27',
            'editor.foreground': '#e2e8f0',
            'editorLineNumber.foreground': '#2a3347',
            'editorLineNumber.activeForeground': '#8899aa',
            'editor.lineHighlightBackground': '#1e2535',
            'editorCursor.foreground': '#38bdf8',
            'editor.selectionBackground': '#38bdf833',
            'editorIndentGuide.background1': '#2a3347',
            'editorWidget.background': '#1e2535',
            'editorSuggestWidget.background': '#1e2535',
         }
      });
      monaco.editor.defineTheme('configui-light', {
         base: 'vs', inherit: true,
         rules: [
            { token: 'string.key.json', foreground: '0284c7' },
            { token: 'string.value.json', foreground: '16a34a' },
            { token: 'number', foreground: 'ea580c' },
            { token: 'keyword.json', foreground: '7c3aed' },
         ],
         colors: {
            'editor.background': '#ffffff',
            'editor.foreground': '#1e293b',
            'editorLineNumber.foreground': '#cbd5e1',
            'editor.lineHighlightBackground': '#e8edf4',
            'editorCursor.foreground': '#0284c7',
            'editor.selectionBackground': '#0284c722',
         }
      });

      const isDark = document.documentElement.getAttribute('data-theme') !== 'light';
      _monacoInstance = monaco.editor.create(
         document.getElementById('monaco-editor-container'),
         {
            language: 'json', theme: isDark ? 'configui-dark' : 'configui-light',
            automaticLayout: true, minimap: { enabled: false },
            scrollBeyondLastLine: false, fontSize: 13,
            fontFamily: "'JetBrains Mono', 'Fira Code', 'Cascadia Code', monospace",
            lineNumbers: 'on', renderWhitespace: 'selection',
            bracketPairColorization: { enabled: true }, formatOnPaste: true,
            tabSize: 2, wordWrap: 'off',
            quickSuggestions: { other: false, comments: false, strings: false },
            parameterHints: { enabled: false }, suggestOnTriggerCharacters: false,
            readOnly: window.READONLY,
         }
      );
      _monacoInstance.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => saveRaw());
      window.__monacoNs = monaco;
      updateMonacoStatusBadge(true);
   })();
   return _monacoInitPromise;
}

async function setRawEditorContent(content) {
   await initMonaco();
   if (_monacoFallback) { document.getElementById('raw-editor-ta').value = content; return; }
   const monaco = window.__monacoNs;
   if (!_monacoModels[currentFile.fileName]) {
      const uri = monaco.Uri.parse(`file:///${currentFile.fileName}`);
      const model = monaco.editor.createModel(content, 'json', uri);
      _monacoModels[currentFile.fileName] = model;
   } else {
      const existing = _monacoModels[currentFile.fileName];
      if (existing.getValue() !== content) existing.setValue(content);
   }
   _monacoInstance.setModel(_monacoModels[currentFile.fileName]);
   syncMonacoTheme();
}

function getRawEditorContent() {
   return _monacoFallback
      ? document.getElementById('raw-editor-ta').value
      : (_monacoInstance?.getValue() ?? '');
}

function showMonacoFallback() {
   document.getElementById('monaco-editor-container').classList.add('hidden');
   document.getElementById('raw-editor-ta').classList.remove('hidden');
   toast('Monaco Editor could not load from CDN — using basic editor.', 'warn', 6000);
}

function updateMonacoStatusBadge(loaded) {
   const badge = document.getElementById('monaco-status');
   if (!badge) return;
   if (loaded) {
      badge.textContent = '⚡ Monaco';
      badge.title = 'Monaco Editor — VS Code engine';
      badge.classList.add('monaco-loaded');
   } else {
      badge.textContent = '⚠ Basic editor';
      badge.title = 'Monaco could not load from CDN';
      badge.classList.add('monaco-fallback');
   }
}

function syncMonacoTheme() {
   if (!_monacoInstance || _monacoFallback || !window.__monacoNs) return;
   const isDark = document.documentElement.getAttribute('data-theme') !== 'light';
   window.__monacoNs.editor.setTheme(isDark ? 'configui-dark' : 'configui-light');
}

// ── Raw editor actions ────────────────────────────────────────────────────
function formatRaw() {
   if (_monacoFallback) {
      const ta = document.getElementById('raw-editor-ta');
      try { ta.value = JSON.stringify(JSON.parse(ta.value), null, 2); }
      catch { toast('Invalid JSON — cannot format', 'error'); }
      return;
   }
   _monacoInstance?.getAction('editor.action.formatDocument')?.run();
}

async function saveRaw() {
   const content = getRawEditorContent();
   try { JSON.parse(content); } catch { toast('Invalid JSON — fix errors before saving', 'error'); return; }
   const r = await api('PUT', `/files/${enc(currentFile.fileName)}/raw`, { content });
   if (r.success) toast('Saved ✓', 'success');
   else toast(r.error, 'error');
}

// ── Editor mode switching ─────────────────────────────────────────────────
function setEditorMode(mode) {
   editorMode = mode;
   activeEditorPath = null;
   document.getElementById('toggle-tree').classList.toggle('active', mode === 'tree');
   document.getElementById('toggle-raw').classList.toggle('active', mode === 'raw');
   document.getElementById('tree-view').classList.add('hidden');
   document.getElementById('raw-view').classList.add('hidden');
   if (currentFile) loadEntries();
}

// ── View switching ────────────────────────────────────────────────────────
// Fix #2 — setView() never touches toolbar visibility.
// The toolbar is owned entirely by selectFile() (show) and _showNoFileState() (hide).
function setView(v) {
   currentView = v;
   // All known view panels — including the new 'guide' panel
   ['editor', 'swap', 'diff', 'guide'].forEach(id => {
      document.getElementById('view-' + id).classList.toggle('hidden', id !== v);
      const navEl = document.getElementById('nav-' + id);
      if (navEl) navEl.classList.toggle('active', id === v);
   });

   // When switching TO the editor with no file selected: hide toolbar & show landing state
   if (v === 'editor' && !currentFile) {
      _showNoFileState();
   }

   // When switching away from editor: hide both toolbar rows
   // (they re-appear when selectFile() is called)
   if (v !== 'editor') {
      document.getElementById('editor-toolbar-row1').classList.add('hidden');
      document.getElementById('editor-toolbar-row2').classList.add('hidden');
   } else if (currentFile) {
      // Switching back to editor and a file is already selected — restore toolbar
      document.getElementById('editor-toolbar-row1').classList.remove('hidden');
      document.getElementById('editor-toolbar-row2').classList.remove('hidden');
   }
}

// ── Swap ──────────────────────────────────────────────────────────────────

// Populates only the swap <select> elements.
// Called once on first load. After executeSwap() we call refreshSwapKeysOnly()
// instead so the source/target/operation selection is preserved.
function populateSwapSelects() {
   ['swap-source', 'swap-target'].forEach(id => {
      document.getElementById(id).innerHTML =
         `<option disabled selected> - SELECT FILE - </option>` +
         files.map(f => `<option value="${escAttr(f.fileName)}">${escHtml(f.fileName)}</option>`).join('');
   });
}

// Populates only the diff <select> elements (always safe to replace).
function populateDiffSelects() {
   ['diff-source', 'diff-target'].forEach(id => {
      document.getElementById(id).innerHTML =
         `<option disabled selected> - SELECT FILE - </option>` +
         files.map(f => `<option value="${escAttr(f.fileName)}">${escHtml(f.fileName)}</option>`).join('');
   });
}

async function loadSwapKeys() {
   const source = document.getElementById('swap-source').value;
   const r = await api('GET', `/files/${enc(source)}/entries`);
   const container = document.getElementById('swap-keys');
   if (!r.success) { container.innerHTML = `<span class="text-red">${escHtml(r.error)}</span>`; return; }
   const flat = flattenEntries(r.data, '');
   if (!flat.length) { container.innerHTML = '<span class="text-muted">No keys found.</span>'; return; }
   container.innerHTML = flat.map(k =>
      `<label class="key-checkbox">
         <input type="checkbox" value="${escAttr(k)}" />
         <span class="key-checkbox-label">${escHtml(k)}</span>
       </label>`).join('');
}

function flattenEntries(entries, prefix) {
   const result = [];
   for (const e of entries) {
      const fp = prefix ? `${prefix}:${e.key}` : e.key;
      if (e.valueType === 'object' && e.children?.length) result.push(...flattenEntries(e.children, fp));
      else result.push(fp);
   }
   return result;
}

async function executeSwap() {
   const sourceEl = document.getElementById('swap-source');
   const targetEl = document.getElementById('swap-target');
   const opEl = document.getElementById('swap-op');
   const source = sourceEl.value;
   const target = targetEl.value;
   const op = opEl.value;
   const overwrite = document.getElementById('swap-overwrite').checked;
   const keys = [...document.querySelectorAll('#swap-keys input:checked')].map(i => i.value);

   if (!keys.length) { toast('Select at least one key', 'warn'); return; }
   if (source === target) { toast('Source and target must be different', 'warn'); return; }

   showModal(
      `${op} ${keys.length} key(s)?`,
      `<strong>${op}</strong> selected keys from <strong>${escHtml(source)}</strong> to <strong>${escHtml(target)}</strong>?`,
      async () => {
         const r = await api('POST', '/swap', { sourceFile: source, targetFile: target, keys, operation: op, overwriteExisting: overwrite });
         if (r.success) {
            toast(`${op} complete ✓`, 'success');

            // Fix #1 — preserve form state after a successful swap.
            // We need to update the internal file list (for diff selects etc.)
            // but we must NOT re-render the swap <select> elements, which would
            // wipe the user's source/target choice and clear the key list.
            const savedSource = source;
            const savedTarget = target;
            const savedOp = op;

            // Refresh file metadata quietly
            const filesR = await api('GET', '/files');
            if (filesR.success) {
               files = filesR.data;
               renderFileList();
               populateDiffSelects();   // safe — diff selects are separate elements
               updateWelcomeStats();
            }

            // Restore the swap form selections
            sourceEl.value = savedSource;
            targetEl.value = savedTarget;
            opEl.value = savedOp;

            // Reload the key list for the source file so moved keys are gone
            // (for Move operations) or still shown (for Copy).
            await loadSwapKeys();
         } else {
            toast(r.error, 'error');
         }
      }
   );
}

// ── Diff ──────────────────────────────────────────────────────────────────
async function runDiff() {
   const source = document.getElementById('diff-source').value;
   const target = document.getElementById('diff-target').value;
   const r = await api('GET', `/diff?source=${enc(source)}&target=${enc(target)}`);
   const container = document.getElementById('diff-result');
   if (!r.success) { container.innerHTML = `<div class="text-red">${escHtml(r.error)}</div>`; return; }
   const d = r.data;
   const rows = [
      ...d.onlyInSource.map(k => `<tr><td class="diff-only-src monospace">${escHtml(k)}</td><td class="diff-only-src">Only in source</td><td>—</td></tr>`),
      ...d.onlyInTarget.map(k => `<tr><td class="diff-only-tgt monospace">${escHtml(k)}</td><td>—</td><td class="diff-only-tgt">Only in target</td></tr>`),
      ...d.valueDifferences.map(e => `<tr><td class="diff-changed monospace">${escHtml(e.key)}</td><td class="diff-changed">${escHtml(e.sourceValue ?? 'null')}</td><td class="diff-changed">${escHtml(e.targetValue ?? 'null')}</td></tr>`),
      ...d.identical.map(k => `<tr><td class="diff-same monospace">${escHtml(k)}</td><td colspan="2" class="diff-same">identical</td></tr>`)
   ];
   container.innerHTML = rows.length
      ? `<table class="diff-table"><thead><tr><th>Key</th><th>${escHtml(source)}</th><th>${escHtml(target)}</th></tr></thead><tbody>${rows.join('')}</tbody></table>`
      : '<div class="text-muted" style="padding:16px">Files are identical.</div>';
}

// ── Helpers ───────────────────────────────────────────────────────────────
function showEditorLoading(show) {
   document.getElementById('editor-loading').style.display = show ? 'flex' : 'none';
   document.getElementById('tree-view').classList.toggle('hidden', show || editorMode !== 'tree');
   document.getElementById('raw-view').classList.toggle('hidden', show || editorMode !== 'raw');
   // While loading, hide the "no file" state too
   if (show) document.getElementById('editor-no-file').classList.add('hidden');
}

function toggleTheme() {
   const html = document.documentElement;
   const dark = html.getAttribute('data-theme') === 'dark';
   html.setAttribute('data-theme', dark ? 'light' : 'dark');
   document.querySelector('[onclick="toggleTheme()"]').textContent = dark ? '🌕' : '🌙';
   syncMonacoTheme();
}

function toast(msg, type = 'success', dur = 3200) {
   const c = document.getElementById('toast-container');
   const el = document.createElement('div');
   el.className = `toast ${type}`;
   el.innerHTML = `<span>${type === 'success' ? '✓' : type === 'error' ? '✕' : '⚠'}</span> <span>${escHtml(msg)}</span>`;
   c.appendChild(el);
   setTimeout(() => el.remove(), dur);
}

function showModal(title, body, onConfirm) {
   document.getElementById('modal-title').innerHTML = title;
   document.getElementById('modal-body').innerHTML = body;
   document.getElementById('modal-backdrop').classList.remove('hidden');
   document.getElementById('modal-confirm').onclick = async () => { closeModal(); await onConfirm(); };
}
function closeModal() { document.getElementById('modal-backdrop').classList.add('hidden'); }

function enc(s) { return encodeURIComponent(s); }
function escHtml(s) { return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }
function escAttr(s) { return String(s ?? '').replace(/'/g, "\\'").replace(/"/g, '&quot;'); }