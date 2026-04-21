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
const _token = new URLSearchParams(window.location.search).get('token') || '';

// ── Monaco state ──────────────────────────────────────────────────────────
let _monacoInstance = null;
let _monacoModels = {};
let _monacoFallback = false;
let _monacoInitPromise = null;

// ── Right Sidebar ─────────────────────────────────────────────────────────
let _rsTab = 'backups';
let _rsResizing = false;

// ── Boot ──────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', async () => {
   if (window.READONLY) {
      document.getElementById('readonly-badge').classList.remove('hidden');
      document.getElementById('add-btn').disabled = true;
      document.getElementById('backup-btn').disabled = true;
   }
   if (!window.AUDIT_ENABLED) {
      document.getElementById('rs-tab-audit').style.display = 'none';
      document.getElementById('rs-panel-audit').classList.add('hidden');
   }
   await loadFileList();
});

// ── API ───────────────────────────────────────────────────────────────────
async function api(method, path, body) {
   const sep = path.includes('?') ? '&' : '?';
   const url = window.CONFIG_UI_API_BASE + path + (_token ? `${sep}token=${encodeURIComponent(_token)}` : '');
   const opts = { method, headers: { 'Content-Type': 'application/json' } };
   if (body !== undefined) opts.body = JSON.stringify(body);
   if (_token) opts.headers['X-Config-Token'] = _token;
   const res = await fetch(url, opts);
   return res.json();
}

// ── Files ─────────────────────────────────────────────────────────────────
async function loadFileList() {
   const r = await api('GET', '/files');
   if (!r.success) { toast('Failed to load files: ' + r.error, 'error'); return; }
   files = r.data;

   _lastKnownTimestamps = Object.fromEntries(files.map(f => [f.fileName, new Date(f.lastModified).getTime()]));
   startHotReloadPolling();

   renderFileList();
   populateDiffSelects();
   if (!_swapSelectsPopulated) {
      populateSwapSelects();
      _swapSelectsPopulated = true;
   }
   updateWelcomeStats();
}

// ── Hot reload detection ──────────────────────────────────────────────────
let _lastKnownTimestamps = {};
let _hotReloadInterval = null;
let _hotReloadDismissed = false;

function startHotReloadPolling() {
   if (_hotReloadInterval) return;
   _hotReloadInterval = setInterval(checkHotReload, 1000000);
}

function stopHotReloadPolling() {
   clearInterval(_hotReloadInterval);
   _hotReloadInterval = null;
}

async function checkHotReload() {
   if (!currentFile) return;
   try {
      const r = await api('GET', '/hot-reload-status');
      if (!r.success) return;

      const changed = Object.entries(r.data).filter(([name, ts]) =>
         _lastKnownTimestamps[name] !== undefined &&
         _lastKnownTimestamps[name] !== ts
      ).map(([name]) => name);

      _lastKnownTimestamps = { ...r.data };

      if (changed.length === 0) return;

      if (!_hotReloadDismissed)
         showHotReloadBanner(changed);

      if (changed.includes(currentFile?.fileName))
         await loadEntries();

   } catch { /* network blip — ignore */ }
}

function showHotReloadBanner(changedFiles) {
   document.getElementById('hot-reload-banner')?.remove();

   const banner = document.createElement('div');
   banner.id = 'hot-reload-banner';
   banner.className = 'hot-reload-banner';
   banner.innerHTML = `
      <span>🔄 <strong>${changedFiles.join(', ')}</strong> was modified externally and has been reloaded.</span>
      <button class="btn btn-ghost btn-sm" onclick="dismissHotReloadBanner()" style="margin-left:auto">✕ Dismiss</button>
   `;

   const panel = document.getElementById('view-editor');
   if (panel) panel.insertAdjacentElement('afterbegin', banner);

   setTimeout(dismissHotReloadBanner, 8000);
}

function dismissHotReloadBanner() {
   document.getElementById('hot-reload-banner')?.remove();
   _hotReloadDismissed = true;
   setTimeout(() => { _hotReloadDismissed = false; }, 30000);
}

let _swapSelectsPopulated = false;

function updateWelcomeStats() {
   const count = files.length;
   const envs = new Set(files.map(f => f.environment)).size;
   const sorted = [...files].sort((a, b) => new Date(b.lastModified) - new Date(a.lastModified));
   const modText = sorted.length
      ? `${sorted[0].fileName} · ${timeAgo(sorted[0].lastModified)}`
      : '–';

   _setText('ws-files', count);
   _setText('ws-envs', envs);
   _setText('ws-modified', modText);
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

   document.getElementById('editor-toolbar-row1').classList.remove('hidden');
   document.getElementById('editor-toolbar-row2').classList.remove('hidden');
   document.getElementById('toolbar-title').innerHTML =
      `<strong>${escHtml(fileName)}</strong> <span class="text-muted monospace" style="font-size:11px">— ${escHtml(currentFile.environment)}</span>`;

   document.getElementById('editor-no-file').classList.add('hidden');

   setView('editor');
   await loadEntries();

   if (document.getElementById('right-sidebar').style.display !== 'none') {
      document.getElementById('rs-file-label').textContent = fileName;
      loadRightSidebar();
   }
}

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
   await validateCurrentFile();
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
   const isArr = node.valueType === 'array';
   const isArrItem = node.valueType === 'arrayItem';
   const isExpandable = (isObj || isArr) && (node.children?.length > 0 || isArr);
   const isExpanded = expandedPaths.has(fullPath);
   const isRevealed = revealedPaths.has(fullPath);

   let valueHtml = '';
   if (isObj) {
      const count = node.children?.length ?? 0;
      valueHtml = `<span class="tree-type-badge" style="margin-left:4px">{${count}}</span>`;
   } else if (isArr) {
      const count = node.children?.length ?? 0;
      valueHtml = `<span class="tree-type-badge" style="margin-left:4px">[${count}]</span>`;
   } else {
      let rawVal = node.rawValue;
      let displayVal;
      if (node.isMasked && !isRevealed) {
         displayVal = '••••••••';
      } else if (node.isMasked && isRevealed) {
         displayVal = revealedValues[fullPath.trim()] ?? '';
      } else if (rawVal === null || rawVal === undefined || rawVal === 'null') {
         displayVal = 'null';
      } else if (node.valueType === 'string' || node.valueType === 'arrayItem') {
         displayVal = (rawVal.startsWith('"') && rawVal.endsWith('"'))
            ? rawVal.slice(1, -1) : rawVal;
      } else {
         displayVal = rawVal;
      }
      const cls = (node.isMasked && !isRevealed) ? 'masked'
         : isArrItem ? 'string'
            : node.valueType;
      valueHtml = `<span class="tree-colon">:</span><span class="tree-value ${cls}">${escHtml(displayVal)}</span>`;
      if (node.isMasked) {
         valueHtml += ` <button class="btn btn-ghost btn-sm" onclick="toggleReveal('${escAttr(fullPath)}',event)"
                style="padding:1px 5px;font-size:11px">${isRevealed ? '🙈 hide' : '👁 reveal'}</button>`;
      }
   }

   const toggleHtml = isExpandable
      ? `<span class="tree-toggle" onclick="toggleExpand('${escAttr(fullPath)}',event)">${isExpanded ? '▾' : '▸'}</span>`
      : `<span class="tree-toggle"></span>`;

   const rowClick = isExpandable ? `onclick="toggleExpand('${escAttr(fullPath)}',event)"` : '';

   let actions = '';
   if (!window.READONLY) {
      if (isArr) {
         actions = `<span class="row-actions">
            <button class="btn btn-secondary btn-sm" onclick="promptAppendArrayItem('${escAttr(fullPath)}',event)" title="Append item">+ item</button>
            <button class="btn btn-danger btn-sm"    onclick="confirmDelete('${escAttr(fullPath)}',event)" title="Delete array">✕</button>
         </span>`;
      } else if (isArrItem) {
         const parentArr = parentPath;
         const idx = node.arrayIndex ?? parseInt(node.key, 10);
         actions = `<span class="row-actions">
            <button class="btn btn-danger btn-sm" onclick="confirmDeleteArrayItem('${escAttr(parentArr)}',${idx},event)" title="Remove item">✕</button>
         </span>`;
      } else {
         actions = `<span class="row-actions">
            <button class="btn btn-danger btn-sm" onclick="confirmDelete('${escAttr(fullPath)}',event)">✕</button>
         </span>`;
      }
   }

   let childHtml = '';
   if (isExpanded && node.children?.length) {
      if (isArr) {
         childHtml = `<div>${renderArrayItems(node.children, depth + 1, fullPath)}</div>`;
      } else {
         childHtml = `<div>${renderNodes(node.children, depth + 1, fullPath)}</div>`;
      }
   } else if (isArr && isExpanded && (!node.children || node.children.length === 0)) {
      if (!window.READONLY) {
         childHtml = `<div style="padding-left:${8 + (depth + 1) * 20}px;padding-top:4px">
            <button class="btn btn-secondary btn-sm" onclick="promptAppendArrayItem('${escAttr(fullPath)}',event)">+ Add first item</button>
         </div>`;
      }
   }

   const canEdit = !window.READONLY && !isArr && !(isObj && isArrItem);
   const dblClick = canEdit ? `ondblclick="startEdit('${escAttr(fullPath)}',${isObj || isArrItem && node.valueType === 'object'},event)"` : '';

   return `<div class="tree-node">
            <div class="tree-row" style="padding-left:${8 + indent}px;cursor:${window.READONLY ? 'default' : 'pointer'}" ${rowClick} ${dblClick}
              title="${canEdit ? 'Double-click to edit' : ''}">
              ${toggleHtml}
              <span class="tree-key">${escHtml(node.key)}</span>
              ${valueHtml}
              ${actions}
            </div>
            <div id="${slotId}" class="hidden"></div>
            ${childHtml}
          </div>`;
}

function renderArrayItems(items, depth, arrayPath) {
   return items.map(item => {
      return renderNode(item, depth, arrayPath);
   }).join('');
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
      if (raw === 'null' || raw === null || raw === undefined) {
         prefill = '';
      } else if ((entry?.valueType === 'string' || entry?.valueType === 'arrayItem')
         && raw.startsWith('"') && raw.endsWith('"')) {
         prefill = raw.slice(1, -1);
      } else {
         prefill = raw;
      }
   }
   const inputId = 'ei-' + pathToId(fullPath);
   const indentPx = 8 + (fullPath.split(':').length - 1) * 20;
   const isMultiline = prefill.includes('\n') || prefill.length > 80;
   slot.innerHTML = `
      <div class="inline-editor" tabindex="-1" style="margin:2px 8px 4px ${indentPx}px;flex-wrap:wrap">
        <span class="tree-key monospace" style="font-size:11px;flex-shrink:0;margin-right:4px;color:var(--accent2);width:100%;margin-bottom:4px">${escHtml(fullPath)}</span>
        ${isMultiline
         ? `<textarea class="inline-input" id="${inputId}" rows="3"
               style="resize:vertical;width:100%;min-height:60px"
               onkeydown="handleEditKeyTextarea(event,'${escAttr(fullPath)}')">${escHtml(prefill)}</textarea>`
         : `<input class="inline-input" id="${inputId}" value="${escAttr(prefill)}"
               placeholder="${entry?.valueType === 'null' ? 'null' : ''}"
               onkeydown="handleEditKey(event,'${escAttr(fullPath)}')" />`
      }
        <button class="btn btn-primary btn-sm" onclick="_actionTaken=true;submitEdit('${escAttr(fullPath)}')">Save</button>
        <button class="btn btn-ghost btn-sm"   onclick="cancelEdit()">✕</button>
      </div>`;
   slot.classList.remove('hidden');
   const inp = document.getElementById(inputId);
   if (inp) {
      inp.focus(); inp.select();
      let _actionTaken = false;
      inp.addEventListener('keydown', e => {
         if (e.key === 'Enter' || e.key === 'Escape') _actionTaken = true;
      });
      inp.parentElement.parentElement.addEventListener('focusout', (event) => {
         const container = event.currentTarget;
         const newFocus = event.relatedTarget;

         // If focus moved to the container itself OR something inside it, do nothing
         if (newFocus && (container === newFocus || container.contains(newFocus))) {
            return;
         }

         // Your existing logic...
         setTimeout(() => {
            const capturedValue = inp.value;
            if (_actionTaken) return;

            if (capturedValue !== prefill && autoSaveOnBlur) {
               showSaveStatus();
               submitEditWithValue(fullPath, capturedValue);
            } else {
               cancelEdit();
            }
         }, 100);
      });
   }
}

function handleEditKey(e, fullPath) {
   if (e.key === 'Enter') { showSaveStatus(); submitEdit(fullPath); }
   if (e.key === 'Escape') cancelEdit();
}

function handleEditKeyTextarea(e, fullPath) {
   if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') { showSaveStatus(); submitEdit(fullPath); }
   if (e.key === 'Escape') cancelEdit();
}

async function submitEdit(fullPath) {
   const inputId = 'ei-' + pathToId(fullPath);
   const inp = document.getElementById(inputId);
   if (!inp) return;
   await submitEditWithValue(fullPath, inp.value);
}

async function submitEditWithValue(fullPath, rawValue) {
   const entry = findEntry(currentEntries, fullPath);
   const isObj = entry?.valueType === 'object';
   let jsonValue = rawValue;
   if (!isObj) {
      if (jsonValue === '' || jsonValue.toLowerCase() === 'null') {
         jsonValue = 'null';
      } else if (entry?.valueType === 'string' || entry?.valueType === 'arrayItem') {
         try { JSON.parse(jsonValue); } catch { jsonValue = JSON.stringify(jsonValue); }
      }
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
      const part = parts[i];
      found = (current ?? []).find(e => e.key === part || String(e.arrayIndex) === part);
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

      monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
         validate: true,
         allowComments: true,
         trailingCommas: 'ignore',
      });

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
   // Check schema violations first — block on errors, allow warnings through
   const r = await api('GET', `/validate?file=${enc(currentFile.fileName)}`);
   if (r?.success && r.data?.some(v => v.severity === 'error')) {
      toast('Save blocked: schema validation errors exist. Fix them or disable schema validation.', 'error', 5000);
      await validateCurrentFile(); // refresh banner
      return;
   }
   const content = getRawEditorContent();
   const result = await api('PUT', `/files/${enc(currentFile.fileName)}/raw`, { content });
   if (result.success) toast('Saved ✓', 'success');
   else toast(result.error, 'error');
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
function setView(v) {
   currentView = v;
   ['editor', 'swap', 'diff', 'guide', 'tests'].forEach(id => {
      document.getElementById('view-' + id).classList.toggle('hidden', id !== v);
      const navEl = document.getElementById('nav-' + id);
      if (navEl) navEl.classList.toggle('active', id === v);
   });

   if (v === 'editor' && !currentFile) {
      _showNoFileState();
   }

   if (v !== 'editor') {
      document.getElementById('editor-toolbar-row1').classList.add('hidden');
      document.getElementById('editor-toolbar-row2').classList.add('hidden');
   } else if (currentFile) {
      document.getElementById('editor-toolbar-row1').classList.remove('hidden');
      document.getElementById('editor-toolbar-row2').classList.remove('hidden');
   }

   if (v == 'swap') {
      document.getElementById('swap-keys').innerHTML = '';
      document.getElementById('swap-source').selectedIndex = 0;
      document.getElementById('swap-target').selectedIndex = 0;
      document.getElementById('swap-overwrite').checked = false;
   }
   else if (v == 'diff') {
      document.getElementById('diff-result').innerHTML = '';
      document.getElementById('diff-source').selectedIndex = 0;
      document.getElementById('diff-target').selectedIndex = 0;
   }
}

// ── Swap ──────────────────────────────────────────────────────────────────
function populateSwapSelects() {
   ['swap-source', 'swap-target'].forEach(id => {
      document.getElementById(id).innerHTML =
         `<option disabled selected> - SELECT FILE - </option>` +
         files.map(f => `<option value="${escAttr(f.fileName)}">${escHtml(f.fileName)}</option>`).join('');
   });
}

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

            const savedSource = source;
            const savedTarget = target;
            const savedOp = op;

            const filesR = await api('GET', '/files');
            if (filesR.success) {
               files = filesR.data;
               renderFileList();
               populateDiffSelects();
               updateWelcomeStats();
            }

            sourceEl.value = savedSource;
            targetEl.value = savedTarget;
            opEl.value = savedOp;

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

// ── Array operations ──────────────────────────────────────────────────────
function promptAppendArrayItem(arrayPath, e) {
   e.stopPropagation();
   showModal(
      `Append item to array`,
      `<div class="form-field" style="width:100%">
         <span class="field-label">Value (string, number, true/false, null, or JSON object/array)</span>
         <textarea id="modal-array-value" class="form-input" rows="3"
            style="width:100%;resize:vertical;font-family:var(--font);font-size:12px"
            placeholder='e.g.  "https://example.com"  or  42  or  {"key":"val"}'></textarea>
       </div>`,
      async () => {
         const val = document.getElementById('modal-array-value')?.value?.trim() ?? '';
         const r = await api('POST', `/files/${enc(currentFile.fileName)}/array-append`,
            { keyPath: arrayPath, jsonValue: val });
         if (r.success) {
            toast('Item appended ✓', 'success');
            expandedPaths.add(arrayPath);
            await loadEntries();
         } else toast(r.error, 'error');
      }
   );
   setTimeout(() => document.getElementById('modal-array-value')?.focus(), 50);
}

function confirmDeleteArrayItem(arrayPath, index, e) {
   e.stopPropagation();
   showModal(
      'Remove array item?',
      `Remove item at index <strong>${index}</strong> from <strong class="monospace">${escHtml(arrayPath)}</strong>?`,
      async () => {
         const r = await api('DELETE',
            `/files/${enc(currentFile.fileName)}/array-item?key=${enc(arrayPath)}&index=${index}`);
         if (r.success) { toast('Item removed ✓', 'success'); await loadEntries(); }
         else toast(r.error, 'error');
      }
   );
}

function openRightSidebar() {
   if (!currentFile) { toast('Select a file first', 'warn'); return; }
   const rs = document.getElementById('right-sidebar');
   rs.style.display = 'flex';
   document.getElementById('rs-file-label').textContent = currentFile.fileName;
   loadRightSidebar();
}

function closeRightSidebar() {
   document.getElementById('right-sidebar').style.display = 'none';
}

function setRsTab(tab) {
   _rsTab = tab;
   ['backups', 'audit'].forEach(t => {
      document.getElementById('rs-tab-' + t).classList.toggle('active', t === tab);
      document.getElementById('rs-panel-' + t).classList.toggle('hidden', t !== tab);
   });
   if (tab === 'audit') loadAuditLog();
   else loadBackupList();
}

async function loadRightSidebar() {
   if (_rsTab === 'backups') await loadBackupList();
   else await loadAuditLog();
}

// ── Backup list ───────────────────────────────────────────────────────────
async function loadBackupList() {
   if (!currentFile) return;
   const container = document.getElementById('backup-list');
   container.innerHTML = '<div class="rs-loading"><div class="spinner" style="width:18px;height:18px;border-width:2px"></div></div>';
   const r = await api('GET', `/files/${enc(currentFile.fileName)}/backups`);
   if (!r.success) { container.innerHTML = `<div class="rs-empty text-red">${escHtml(r.error)}</div>`; return; }
   if (!r.data.length) { container.innerHTML = '<div class="rs-empty">No backups yet.</div>'; return; }
   // r.data is now just file names (not full paths) — FIX #3
   container.innerHTML = r.data.map(name => {
      const label = parseBackupLabel(name);
      const date = parseBackupDate(name);
      return `<div class="backup-item">
            <div class="backup-item-info">
                <span class="backup-name">${escHtml(label)}</span>
                <span class="backup-date">${escHtml(date)}</span>
            </div>
            <div class="backup-item-actions">
                <button class="btn btn-ghost btn-sm" title="Diff" onclick='showDiffModal("${name}")'>⊞</button>
                <button class="btn btn-secondary btn-sm" title="Restore" onclick='confirmRestore("${name}")'>↩</button>
                <button class="btn btn-danger btn-sm" title="Delete" onclick='confirmDeleteBackup("${name}")'>✕</button>
            </div>
        </div>`;
   }).join('');
}

// FIX #3: work with file names only — no full paths needed on the frontend.
// The backend resolves names to paths via ConfigUIOptions.ConfigDirectory.
function parseBackupLabel(fileName) {
   // e.g. "appsettings.json.v1.2.bak" → "v1.2"
   //      "appsettings.json.20240101T120000.bak" → "20240101T120000"
   const withoutBak = fileName.endsWith('.bak') ? fileName.slice(0, -4) : fileName;
   const m = withoutBak.match(/appsettings(?:\.[^.]+)?\.json\.(.+)$/i);
   return m ? m[1] : withoutBak;
}

function parseBackupDate(fileName) {
   const label = parseBackupLabel(fileName);
   const m = label.match(/^(\d{4})(\d{2})(\d{2})[T-](\d{2})(\d{2})(\d{2})$/);
   if (m) {
      const d = new Date(`${m[1]}-${m[2]}-${m[3]}T${m[4]}:${m[5]}:${m[6]}Z`);
      return isNaN(d) ? label : d.toLocaleString();
   }
   return '';
}

// Keep old aliases so any remaining call sites still work
function parseBackupName(p) { return parseBackupLabel(p); }

// ── Named backup dialog ───────────────────────────────────────────────────
async function promptNamedBackup() {
   if (!currentFile) return;
   const sr = await api('POST', `/files/${enc(currentFile.fileName)}/backup/suggest`);
   const suggested = sr.success ? sr.data : new Date().toISOString().slice(0, 10);

   showModal(
      '💾 Create Backup',
      `<div class="form-field" style="width:100%">
            <span class="field-label">Backup name / label</span>
            <input class="form-input" id="modal-backup-name" value="${escAttr(suggested)}"
                style="width:100%" placeholder="e.g. v1.2 or before-payment-refactor" />
            <span style="font-size:11px;color:var(--text2);margin-top:4px;display:block">
                File will be saved as: <code>${escHtml(currentFile.fileName)}.{name}.bak</code>
            </span>
        </div>`,
      async () => {
         const name = document.getElementById('modal-backup-name')?.value?.trim() ?? suggested;
         const r = await api('POST', `/files/${enc(currentFile.fileName)}/backup`, { name });
         if (r.success) { toast('Backup created ✓', 'success'); await loadBackupList(); }
         else toast(r.error, 'error');
      }
   );
   setTimeout(() => {
      const inp = document.getElementById('modal-backup-name');
      if (inp) { inp.focus(); inp.select(); }
   }, 50);
}

// ── Restore ───────────────────────────────────────────────────────────────
function confirmRestore(backupName) {
   const label = parseBackupLabel(backupName);
   showModal(
      'Restore backup?',
      `Restore <strong>${escHtml(currentFile.fileName)}</strong> from backup <strong class="monospace">${escHtml(label)}</strong>?
        <br><br><span style="color:var(--yellow);font-size:12px">⚠ The current file will be backed up automatically before restoring.</span>`,
      async () => {
         const r = await api('POST', `/files/${enc(currentFile.fileName)}/backup/restore`,
            { backupPath: backupName });
         if (r.success) {
            toast('Restored ✓', 'success');
            await loadBackupList();
            await loadEntries();
         } else toast(r.error, 'error');
      }
   );
}

// ── Delete backup ─────────────────────────────────────────────────────────
function confirmDeleteBackup(backupName) {
   const label = parseBackupLabel(backupName);
   showModal(
      'Delete backup?',
      `Permanently delete backup <strong class="monospace">${escHtml(label)}</strong>? This cannot be undone.`,
      async () => {
         const r = await api('DELETE',
            `/files/${enc(currentFile.fileName)}/backup?path=${enc(backupName)}`);
         if (r.success) { toast('Backup deleted ✓', 'success'); await loadBackupList(); }
         else toast(r.error, 'error');
      }
   );
}

// ── FIX #5: Backup diff modal — JSON key-level diff instead of line-by-line ──
async function showDiffModal(backupName) {
   const r = await api('GET',
      `/files/${enc(currentFile.fileName)}/backups/diff?backup=${enc(backupName)}`);
   if (!r.success) { toast(r.error, 'error'); return; }

   const { current, backup } = r.data;
   let cur = {}, bak = {};
   try { cur = flattenJson(JSON.parse(stripJsonComments(current))); } catch { }
   try { bak = flattenJson(JSON.parse(stripJsonComments(backup))); } catch { }

   const allKeys = [...new Set([...Object.keys(bak), ...Object.keys(cur)])].sort();
   let rows = '';
   let hasDiff = false;

   for (const k of allKeys) {
      const inBak = k in bak, inCur = k in cur;
      if (inBak && inCur && bak[k] === cur[k]) continue; // skip identical
      hasDiff = true;
      if (inBak && !inCur)
         rows += `<tr>
            <td class="diff-only-src monospace" style="word-break:break-all">${escHtml(k)}</td>
            <td class="diff-only-src" style="word-break:break-all">${escHtml(bak[k])}</td>
            <td style="color:var(--text2);font-style:italic">removed</td>
         </tr>`;
      else if (!inBak && inCur)
         rows += `<tr>
            <td class="diff-only-tgt monospace" style="word-break:break-all">${escHtml(k)}</td>
            <td style="color:var(--text2);font-style:italic">—</td>
            <td class="diff-only-tgt" style="word-break:break-all">${escHtml(cur[k])}</td>
         </tr>`;
      else
         rows += `<tr>
            <td class="diff-changed monospace" style="word-break:break-all">${escHtml(k)}</td>
            <td class="diff-changed" style="word-break:break-all">${escHtml(bak[k])}</td>
            <td class="diff-changed" style="word-break:break-all">${escHtml(cur[k])}</td>
         </tr>`;
   }

   const label = parseBackupLabel(backupName);
   document.getElementById('modal-title').innerHTML =
      `⊞ Diff — <span class="monospace" style="font-size:13px">${escHtml(label)}</span> vs current`;
   document.getElementById('modal-body').innerHTML = hasDiff
      ? `<div style="overflow-y:auto;max-height:60vh">
            <table class="diff-table" style="width:100%;table-layout:fixed">
               <colgroup><col style="width:35%"><col style="width:32.5%"><col style="width:32.5%"></colgroup>
               <thead><tr><th>Key</th><th>Backup (${escHtml(label)})</th><th>Current</th></tr></thead>
               <tbody>${rows}</tbody>
            </table>
         </div>`
      : '<div class="text-muted" style="padding:16px">Files are identical.</div>';

   document.getElementById('modal-backdrop').classList.remove('hidden');
   document.querySelector('.modal').style.width = 'min(90vw, 780px)';
   document.getElementById('modal-confirm').style.display = 'none';
   document.getElementById('modal-cancel').onclick = _origCloseModal;
}

// FIX #5 helper: flatten a nested JSON object to colon-separated key paths
function flattenJson(obj, prefix) {
   prefix = prefix || '';
   const result = {};
   for (const [k, v] of Object.entries(obj)) {
      const path = prefix ? `${prefix}:${k}` : k;
      if (v !== null && typeof v === 'object' && !Array.isArray(v))
         Object.assign(result, flattenJson(v, path));
      else
         result[path] = JSON.stringify(v);
   }
   return result;
}

// Override closeModal to reset modal width
const _origCloseModal = function () {
   closeModal();
   document.querySelector('.modal').style.width = '';
   document.getElementById('modal-confirm').style.display = '';
}

// ── FIX #2: Audit log — collapsible by key ────────────────────────────────
async function loadAuditLog() {
   if (!currentFile) return;
   const container = document.getElementById('audit-list');
   container.innerHTML = '<div class="rs-loading"><div class="spinner" style="width:18px;height:18px;border-width:2px"></div></div>';
   const r = await api('GET', `/files/${enc(currentFile.fileName)}/audit`);
   if (!r.success) { container.innerHTML = `<div class="rs-empty text-red">${escHtml(r.error)}</div>`; return; }
   if (!r.data.length) {
      container.innerHTML = '<div class="rs-empty">No audit entries yet.<br><span style="font-size:11px">Enable via <code>options.EnableAuditLogging = true</code></span></div>';
      return;
   }

   const opColor = {
      Add: 'var(--green)', Update: 'var(--accent)', Delete: 'var(--red)',
      Restore: 'var(--purple)', SaveRaw: 'var(--orange)',
      AppendArrayItem: 'var(--green)', RemoveArrayItem: 'var(--red)'
   };

   // Group entries by keyPath
   const groups = {};
   r.data.forEach(e => {
      if (!groups[e.keyPath]) groups[e.keyPath] = [];
      groups[e.keyPath].push(e);
   });

   container.innerHTML = Object.entries(groups).map(([keyPath, entries]) => {
      const id = 'agroup-' + pathToId(keyPath);
      const rows = entries.map(e => {
         const color = opColor[e.operation] ?? 'var(--text2)';
         const ts = new Date(e.timestamp).toLocaleString();
         return `<div class="audit-item" style="border:none;background:var(--bg);margin:2px 0;border-radius:4px;padding:6px 8px">
            <div class="audit-header">
                <span class="audit-op" style="color:${color}">${escHtml(e.operation)}</span>
                <span class="audit-ts">${escHtml(ts)}</span>
            </div>
            ${e.oldValue || e.newValue ? `
            <div class="audit-values">
                ${e.oldValue ? `<div class="audit-val old"><span>before:</span> <code>${escHtml(e.oldValue)}</code></div>` : ''}
                ${e.newValue ? `<div class="audit-val new"><span>after:</span> <code>${escHtml(e.newValue)}</code></div>` : ''}
            </div>` : ''}
         </div>`;
      }).join('');

      return `<div class="audit-group">
         <div class="audit-group-header" onclick="toggleAuditGroup('${escAttr(id)}')">
            <span class="tree-toggle" id="${id}-tog">▸</span>
            <span class="audit-key monospace" style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${escHtml(keyPath)}</span>
            <span class="audit-ts" style="flex-shrink:0;margin-left:6px">${entries.length} change${entries.length > 1 ? 's' : ''}</span>
         </div>
         <div id="${id}" class="hidden" style="padding:4px 6px">${rows}</div>
      </div>`;
   }).join('');
}

// FIX #2 helper: toggle a key group open/closed
function toggleAuditGroup(id) {
   const el = document.getElementById(id);
   const tog = document.getElementById(id + '-tog');
   if (!el) return;
   el.classList.toggle('hidden');
   if (tog) tog.textContent = el.classList.contains('hidden') ? '▸' : '▾';
}

// ── Right sidebar resizer ─────────────────────────────────────────────────
(function initResizer() {
   document.addEventListener('DOMContentLoaded', () => {
      const resizer = document.getElementById('rs-resizer');
      if (!resizer) return;
      let startX, startW;
      resizer.addEventListener('mousedown', e => {
         _rsResizing = true;
         startX = e.clientX;
         startW = document.getElementById('right-sidebar').offsetWidth;
         document.body.style.userSelect = 'none';
         document.body.style.cursor = 'col-resize';
      });
      document.addEventListener('mousemove', e => {
         if (!_rsResizing) return;
         const delta = startX - e.clientX;
         const newW = Math.max(220, Math.min(600, startW + delta));
         document.getElementById('right-sidebar').style.width = newW + 'px';
      });
      document.addEventListener('mouseup', () => {
         if (!_rsResizing) return;
         _rsResizing = false;
         document.body.style.userSelect = '';
         document.body.style.cursor = '';
      });
   });
})();

// ── v1.3: Schema Validation ───────────────────────────────────────────────

async function validateCurrentFile() {
   if (!currentFile) return;
   const r = await api('GET', `/validate?file=${enc(currentFile.fileName)}`);
   const banner = document.getElementById('schema-violations');
   if (!r || !r.success || !r.data.length) { banner.classList.add('hidden'); banner.innerHTML = ''; return; }
   const items = r.data.map(v =>
      `<span style="display:inline-flex;align-items:center;gap:6px;margin:2px 4px;
            padding:2px 8px;border-radius:4px;font-size:12px;font-family:var(--font);
            background:${v.severity === 'error' ? 'rgba(248,113,113,.15)' : 'rgba(251,191,36,.15)'};
            border:1px solid ${v.severity === 'error' ? 'var(--red)' : 'var(--yellow)'};color:var(--text)">
          <strong style="color:${v.severity === 'error' ? 'var(--red)' : 'var(--yellow)'}">${escHtml(v.keyword)}</strong>
          <span style="color:var(--accent2)">${escHtml(v.keyPath)}</span>
          — ${escHtml(v.message)}
        </span>`
   ).join('');
   banner.innerHTML = `<div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap">
        <span style="font-size:12px;font-weight:600;color:var(--red);flex-shrink:0">⚠ Schema violations:</span>
        ${items}
        <button class="btn btn-ghost btn-sm" style="margin-left:auto" onclick="document.getElementById('schema-violations').classList.add('hidden')">✕</button>
    </div>`;
   banner.classList.remove('hidden');
}

// Call validateCurrentFile() after loadEntries() completes — add at end of loadEntries:
// await validateCurrentFile();

// Also call it before saveRaw() writes — block save on errors:
// (in saveRaw, check violations first)

// ── v1.3: Assertion Runner ────────────────────────────────────────────────

async function runAssertions() {
   document.getElementById('tests-result').innerHTML =
      '<div class="loading-center" style="height:80px"><div class="spinner"></div></div>';
   document.getElementById('tests-no-tests').classList.add('hidden');
   document.getElementById('tests-summary').textContent = '';

   const r = await api('GET', '/assertions/run');
   if (!r.success) { toast(r.error, 'error'); return; }

   if (!r.hasTests || !r.data.length) {
      document.getElementById('tests-result').innerHTML = '';
      document.getElementById('tests-no-tests').classList.remove('hidden');
      return;
   }

   const passed = r.data.filter(t => t.passed).length;
   const total = r.data.length;
   const allPass = passed === total;
   document.getElementById('tests-summary').innerHTML =
      `<span style="color:${allPass ? 'var(--green)' : 'var(--red)'}">
            ${passed}/${total} passed
        </span>`;

   document.getElementById('tests-result').innerHTML = r.data.map(t => `
        <div style="display:flex;align-items:flex-start;gap:10px;padding:10px 12px;
            margin-bottom:6px;border-radius:6px;border:1px solid var(--border);
            background:var(--bg2);border-left:3px solid ${t.passed ? 'var(--green)' : 'var(--red)'}">
            <span style="font-size:16px;flex-shrink:0">${t.passed ? '✓' : '✕'}</span>
            <div style="flex:1;min-width:0">
                <div style="font-weight:600;font-size:13px;color:${t.passed ? 'var(--green)' : 'var(--red)'}">${escHtml(t.name)}</div>
                ${t.description ? `<div style="font-size:12px;color:var(--text2);margin-top:2px">${escHtml(t.description)}</div>` : ''}
                ${!t.passed && t.failureMessage ? `<div style="font-size:12px;color:var(--red);margin-top:4px;font-family:var(--font)">${escHtml(t.failureMessage)}</div>` : ''}
            </div>
            <span style="font-size:11px;color:var(--text2);flex-shrink:0">${t.elapsedMs}ms</span>
        </div>`).join('');
}

// ── Helpers ───────────────────────────────────────────────────────────────
function showEditorLoading(show) {
   document.getElementById('editor-loading').style.display = show ? 'flex' : 'none';
   document.getElementById('tree-view').classList.toggle('hidden', show || editorMode !== 'tree');
   document.getElementById('raw-view').classList.toggle('hidden', show || editorMode !== 'raw');
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

function stripJsonComments(str) {
   let result = '';
   let i = 0;
   let inString = false;
   while (i < str.length) {
      if (!inString && str[i] === '/' && str[i + 1] === '/') {
         while (i < str.length && str[i] !== '\n') i++;
      } else if (!inString && str[i] === '/' && str[i + 1] === '*') {
         i += 2;
         while (i < str.length && !(str[i] === '*' && str[i + 1] === '/')) i++;
         i += 2;
      } else {
         if (str[i] === '"' && (i === 0 || str[i - 1] !== '\\')) inString = !inString;
         result += str[i++];
      }
   }
   return result;
}

function closeModal() { document.getElementById('modal-backdrop').classList.add('hidden'); }

function enc(s) { return encodeURIComponent(s); }
function escHtml(s) { return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }
function escAttr(s) { return String(s ?? '').replace(/'/g, "\\'").replace(/"/g, '&quot;'); }