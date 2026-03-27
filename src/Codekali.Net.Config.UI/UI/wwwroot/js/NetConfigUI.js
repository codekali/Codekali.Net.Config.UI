// ── State ─────────────────────────────────────────────────────────────────
let files = [];
let currentFile = null;
let currentEntries = [];   // raw API response — nested tree from server
let expandedPaths = new Set();   // Set of full dot-paths that are expanded
let revealedPaths = new Set();   // Set of full dot-paths whose values are revealed
let activeEditorPath = null;     // full dot-path currently being edited inline
let autoSaveOnBlur = true;        // when true, blur on the inline input triggers save
let searchQuery = '';
let editorMode = 'tree';
let currentView = 'editor';
let pendingConfirm = null;
const revealedValues = {};
const _token = new URLSearchParams(window.location.search).get('token') || '';

// ── Boot ──────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', async () => {
   if (READ_ONLY) {
      document.getElementById('readonly-badge').classList.remove('hidden');
      document.getElementById('add-btn').disabled = true;
      document.getElementById('backup-btn').disabled = true;
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
   renderFileList();
   populateSwapDiffSelects();
   updateWelcomeStats();
}

function updateWelcomeStats() {
   // file count
   document.getElementById('ws-files').textContent = files.length;

   // unique environments (excluding duplicates like two Dev files)
   const envs = new Set(files.map(f => f.environment));
   document.getElementById('ws-envs').textContent = envs.size;

   // most recently modified file
   const sorted = [...files].sort((a, b) =>
      new Date(b.lastModified) - new Date(a.lastModified));
   if (sorted.length) {
      const newest = sorted[0];
      const ago = timeAgo(newest.lastModified);
      document.getElementById('ws-modified').textContent =
         `${newest.fileName} · ${ago}`;
   }
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
   status.textContent = "Saving...";
   status.style.opacity = "1";

   // Simulate network delay
   setTimeout(() => {
      status.textContent = "Saved";

      // Fade out after 2 seconds
      setTimeout(() => {
         status.style.opacity = "0";
      }, 2000);
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
   expandedPaths.clear(); revealedPaths.clear();
   Object.keys(revealedValues).forEach(k => delete revealedValues[k]);
   activeEditorPath = null; searchQuery = '';
   document.getElementById('search-input').value = '';
   renderFileList();
   document.getElementById('editor-toolbar-row1').style.display = 'flex';
   document.getElementById('editor-toolbar-row1').classList.remove('hidden');
   document.getElementById('editor-toolbar-row2').classList.remove('hidden');
   document.getElementById('toolbar-title').innerHTML =
      `<strong>${escHtml(fileName)}</strong> <span class="text-muted monospace" style="font-size:11px">— ${escHtml(currentFile.environment)}</span>`;
   setView('editor');
   await loadEntries();
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
      document.getElementById('raw-editor-ta').value = r.data;
      showEditorLoading(false);
      document.getElementById('raw-view').classList.remove('hidden');
   }
}

async function refreshFile() { if (currentFile) { activeEditorPath = null; await loadEntries(); } }

// ── Tree rendering ────────────────────────────────────────────────────────
// Each node is identified by its FULL colon-separated path from the root,
// e.g. "Logging:LogLevel:Default". This avoids collisions between nodes
// that share the same key name at different levels.

function renderTree() {
   const container = document.getElementById('tree-view');
   const entries = searchQuery ? filterEntries(currentEntries, searchQuery, '') : currentEntries;
   container.innerHTML = entries.length
      ? renderNodes(entries, 0, '')
      : '<div class="text-muted" style="padding:16px;font-size:13px">No keys match your search.</div>';
   showEditorLoading(false);
   container.classList.remove('hidden');

   // Restore active inline editor after re-render
   if (activeEditorPath) {
      const slot = document.getElementById('slot-' + pathToId(activeEditorPath));
      if (slot) openInlineEditor(slot, activeEditorPath);
   }
}

function renderNodes(nodes, depth, parentPath) {
   return nodes.map(n => renderNode(n, depth, parentPath)).join('');
}

function renderNode(node, depth, parentPath) {
   // Full colon-separated path — uniquely identifies this node in the entire tree
   const fullPath = parentPath ? `${parentPath}:${node.key}` : node.key;
   const slotId = 'slot-' + pathToId(fullPath);
   const indent = depth * 20;

   const isObj = node.valueType === 'object';
   const isExpanded = expandedPaths.has(fullPath);
   const isRevealed = revealedPaths.has(fullPath);
   const isEditing = activeEditorPath === fullPath;

   // ── Value display ──
   let valueHtml = '';
   if (isObj) {
      // Object section: show count badge; value column is empty
      valueHtml = `<span class="tree-type-badge" style="margin-left:4px">{${node.children?.length ?? 0}}</span>`;
   } else {
      // Determine display value
      let rawVal = node.rawValue;

      // Strip surrounding quotes from JSON strings for display
      let displayVal;
      if (node.isMasked && !isRevealed) {
         displayVal = '••••••••';
      } else if (node.isMasked && isRevealed) {
         displayVal = revealedValues[fullPath.trim()] ?? '';
      } else if (rawVal === null || rawVal === undefined || rawVal === 'null') {
         displayVal = 'null';
      } else if (node.valueType === 'string') {
         displayVal = rawVal.startsWith('"') && rawVal.endsWith('"')
            ? rawVal.slice(1, -1)
            : rawVal;
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

   // ── Expand toggle ──
   const toggleHtml = isObj
      ? `<span class="tree-toggle" onclick="toggleExpand('${escAttr(fullPath)}',event)">${isExpanded ? '▾' : '▸'}</span>`
      : `<span class="tree-toggle"></span>`;

   // ── Row click (expand for objects) ──
   const rowClick = isObj ? `onclick="toggleExpand('${escAttr(fullPath)}',event)"` : '';

   // ── Actions ──
   // Edit is triggered by double-click on the row (see ondblclick on tree-row below).
   // Only a delete button remains visible on hover.
   let actions = '';
   if (!window.READONLY) {
      const delBtn = `<button class="btn btn-danger btn-sm" onclick="confirmDelete('${escAttr(fullPath)}',event)">✕</button>`;
      actions = `<span class="row-actions">${delBtn}</span>`;
   }

   // ── Children ──
   let childHtml = '';
   if (isObj && isExpanded && node.children?.length) {
      childHtml = `<div>${renderNodes(node.children, depth + 1, fullPath)}</div>`;
   }

   const dblClick = window.READONLY ? '' : `ondblclick="startEdit('${escAttr(fullPath)}',${isObj},event)"`;
   return `<div class="tree-node">
            <div class="tree-row" style="padding-left:${8 + indent}px;cursor:${READ_ONLY ? 'default' : 'pointer'}" ${rowClick} ${dblClick}
              title="${window.READONLY ? '' : 'Double-click to edit'}">
              ${toggleHtml}
              <span class="tree-key">${escHtml(node.key)}</span>
              ${valueHtml}
              ${actions}
            </div>
            <div id="${slotId}" class="${isEditing ? '' : 'hidden'}"></div>
            ${childHtml}
          </div>`;
}

// Convert a full colon-path to a safe DOM id (replace non-alphanum with _)
function pathToId(path) {
   return path.replace(/[^a-zA-Z0-9]/g, '_');
}

function toggleExpand(fullPath, e) {
   e.stopPropagation();
   if (expandedPaths.has(fullPath)) expandedPaths.delete(fullPath);
   else expandedPaths.add(fullPath);
   renderTree();
}

async function toggleReveal(fullPath, e) {
   e.stopPropagation();
   if (revealedPaths.has(fullPath)) {
      revealedPaths.delete(fullPath);
      renderTree();
      return;
   }
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
         if (fc.length) {
            expandedPaths.add(fullPath);   // auto-expand matched parent
            result.push({ ...e, children: fc });
         }
      }
   }
   return result;
}

// ── Inline editor ─────────────────────────────────────────────────────────
// fullPath  — the complete colon-path used as the API keyPath
// rawValue  — the JSON-encoded current value (used to pre-fill the input)
// isObj     — when true we edit the section as raw JSON

async function startEdit(fullPath, isObj, e) {
   e.stopPropagation();
   if (activeEditorPath === fullPath) return;

   // If the entry is masked and we don't have the real value yet, fetch it first
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
   // Find the entry's current raw value from the live tree
   const entry = findEntry(currentEntries, fullPath);
   const isObj = entry?.valueType === 'object';

   let prefill = '';
   if (isObj && entry?.children) {
      prefill = entriesToJson(entry.children);
   } else if (entry?.isMasked) {
      // real value was fetched before openInlineEditor was called — use the cache directly
      prefill = revealedValues[fullPath.trim()] ?? '';
   } else {
      const raw = entry?.rawValue ?? '';
      if (entry?.valueType === 'string' && raw.startsWith('"') && raw.endsWith('"')) {
         prefill = raw.slice(1, -1);
      } else {
         prefill = raw;
      }
   }

   const inputId = 'ei-' + pathToId(fullPath);
   const indentPx = 8 + (fullPath.split(':').length - 1) * 20;
   slot.innerHTML = `
            <div class="inline-editor" style="margin:2px 8px 4px ${indentPx}px">
              <span class="tree-key monospace" style="font-size:11px;flex-shrink:0;margin-right:4px;color:var(--accent2)">${escHtml(fullPath)}</span>
              <input class="inline-input" id="${inputId}" value="${escAttr(prefill)}"
                onkeydown="handleEditKey(event,'${escAttr(fullPath)}')" />
              <button class="btn btn-primary btn-sm" onclick="submitEdit('${escAttr(fullPath)}')">Save</button>
              <button class="btn btn-ghost btn-sm" onclick="cancelEdit()">✕</button>
            </div>`;
   slot.classList.remove('hidden');
   const inp = document.getElementById(inputId);
   if (inp) {
      inp.focus(); inp.select();
      // Attach blur handler — save or cancel depending on the toggle
      inp.addEventListener('blur', e => {
         // Only act if focus moved outside the inline-editor entirely
         setTimeout(() => {
            const active = document.activeElement;
            const editor = slot.querySelector('.inline-editor');
            if (editor && !editor.contains(active)) {
               if (autoSaveOnBlur) {
                  showSaveStatus();
                  submitEdit(fullPath);
               }
               else cancelEdit();
            }
         }, 120);   // small delay so Save/Cancel button clicks register first
      });
   }
}

function handleEditKey(e, fullPath) {
   if (e.key === 'Enter') {
      showSaveStatus();
      submitEdit(fullPath);
   }
   if (e.key === 'Escape') cancelEdit();
}

async function submitEdit(fullPath) {
   const inputId = 'ei-' + pathToId(fullPath);
   const inp = document.getElementById(inputId);
   if (!inp) return;

   const entry = findEntry(currentEntries, fullPath);
   const isObj = entry?.valueType === 'object';
   let jsonValue = inp.value;

   // For non-object string values: wrap in quotes if the raw input is not valid JSON
   if (!isObj && entry?.valueType === 'string') {
      try { JSON.parse(jsonValue); }
      catch { jsonValue = JSON.stringify(jsonValue); }   // treat as plain string
   }

   const r = await api('PUT', `/files/${enc(currentFile.fileName)}/entries`, { keyPath: fullPath, jsonValue });
   if (r.success) {
      toast('Saved ✓', 'success');
      activeEditorPath = null;
      await loadEntries();
   } else {
      toast(r.error, 'error');
   }
}

function cancelEdit() {
   activeEditorPath = null;
   renderTree();
}

// Walk the nested entry tree to find a node by its full colon-path
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

// Reconstruct a compact JSON string from a children array (for object pre-fill)
function entriesToJson(children) {
   if (!children) return '{}';
   const obj = {};
   for (const c of children) {
      if (c.valueType === 'object') obj[c.key] = JSON.parse(entriesToJson(c.children));
      else {
         try { obj[c.key] = JSON.parse(c.rawValue ?? 'null'); }
         catch { obj[c.key] = c.rawValue; }
      }
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

// ── Backup (explicit) ─────────────────────────────────────────────────────
async function createBackup() {
   if (!currentFile) { toast('Select a file first', 'warn'); return; }
   const r = await api('POST', `/files/${enc(currentFile.fileName)}/backup`);
   if (r.success) toast(`Backup created ✓`, 'success');
   else toast(r.error, 'error');
}

// ── Raw editor ────────────────────────────────────────────────────────────
function setEditorMode(mode) {
   editorMode = mode;
   activeEditorPath = null;
   document.getElementById('toggle-tree').classList.toggle('active', mode === 'tree');
   document.getElementById('toggle-raw').classList.toggle('active', mode === 'raw');
   document.getElementById('tree-view').classList.add('hidden');
   document.getElementById('raw-view').classList.add('hidden');
   if (currentFile) loadEntries();
}

function formatRaw() {
   const ta = document.getElementById('raw-editor-ta');
   try { ta.value = JSON.stringify(JSON.parse(ta.value), null, 2); }
   catch { toast('Invalid JSON — cannot format', 'error'); }
}

async function saveRaw() {
   const content = document.getElementById('raw-editor-ta').value;
   try { JSON.parse(content); } catch { toast('Invalid JSON — fix errors before saving', 'error'); return; }
   const r = await api('PUT', `/files/${enc(currentFile.fileName)}/raw`, { content });
   if (r.success) toast('Saved ✓', 'success');
   else toast(r.error, 'error');
}

// ── View switching ────────────────────────────────────────────────────────
function setView(v) {
   currentView = v;
   ['editor', 'swap', 'diff'].forEach(id => {
      document.getElementById('view-' + id).classList.toggle('hidden', id !== v);
      document.getElementById('nav-' + id).classList.toggle('active', id === v);
   });
   document.getElementById('editor-toolbar-row1').classList.toggle('hidden', v !== 'editor');
   document.getElementById('editor-toolbar-row2').classList.toggle('hidden', v !== 'editor');
   if (v === 'editor') {
      document.getElementById('toolbar-title').innerHTML = currentFile
         ? `<strong>${escHtml(currentFile.fileName)}</strong>` : 'Select a file';
   } else if (v === 'swap') {
      document.getElementById('toolbar-title').innerHTML = '<strong>Move / Copy Keys</strong>';
   } else {
      document.getElementById('toolbar-title').innerHTML = '<strong>Compare Files</strong>';
   }
}

// ── Swap ──────────────────────────────────────────────────────────────────
function populateSwapDiffSelects() {
   ['swap-source', 'swap-target', 'diff-source', 'diff-target'].forEach(id => {

      document.getElementById(id).innerHTML =
         `<option disabled selected> - SELECT FILE - </option>` +
         files.map(f => `<option value="${escAttr(f.fileName)}">${escHtml(f.fileName)}</option>`).join('');
   });
   /*if (files.length > 1) {
       document.getElementById('swap-target').selectedIndex = 1;
       document.getElementById('diff-target').selectedIndex = 1;
   }*/
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
   const source = document.getElementById('swap-source').value;
   const target = document.getElementById('swap-target').value;
   const op = document.getElementById('swap-op').value;
   const overwrite = document.getElementById('swap-overwrite').checked;
   const keys = [...document.querySelectorAll('#swap-keys input:checked')].map(i => i.value);
   if (!keys.length) { toast('Select at least one key', 'warn'); return; }
   if (source === target) { toast('Source and target must be different', 'warn'); return; }
   showModal(
      `${op} ${keys.length} key(s)?`,
      `<strong>${op}</strong> selected keys from <strong>${escHtml(source)}</strong> to <strong>${escHtml(target)}</strong>?`,
      async () => {
         const r = await api('POST', '/swap', { sourceFile: source, targetFile: target, keys, operation: op, overwriteExisting: overwrite });
         if (r.success) { toast(`${op} complete ✓`, 'success'); await loadFileList(); }
         else toast(r.error, 'error');
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
   const wrap = document.getElementById('editor-loading');
   const empty = document.getElementById('editor-empty-state');
   const spinner = document.getElementById('editor-spinner');
   // The outer wrapper is always visible until a file is loaded; once hidden it stays hidden
   if (show) {
      wrap.style.display = 'flex';
      empty.style.display = 'none';
      spinner.style.display = 'flex';
   } else {
      wrap.style.display = 'none';
      empty.style.display = 'flex';   // restore for next time (doesn't matter, wrap is hidden)
      spinner.style.display = 'none';
   }
   document.getElementById('tree-view').classList.toggle('hidden', show || editorMode !== 'tree');
   document.getElementById('raw-view').classList.toggle('hidden', show || editorMode !== 'raw');
}

function toggleTheme() {
   const html = document.documentElement;
   const dark = html.getAttribute('data-theme') === 'dark';
   html.setAttribute('data-theme', dark ? 'light' : 'dark');
   document.querySelector('[onclick="toggleTheme()"]').textContent = dark ? '🌕' : '🌙';
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