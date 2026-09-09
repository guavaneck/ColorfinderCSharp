const state = { snapshot: null, selectedPaths: new Set(), anchorIndex: -1, home: null, view: 'grid', pending: new Map(), colorFilter: null, query: '' };
const entriesElement = document.querySelector('#entries');
const contextMenu = document.querySelector('#context-menu');
const appDialog = document.querySelector('#app-dialog');
const dialogInput = document.querySelector('#dialog-input');
const folderPicker = document.querySelector('#folder-picker');
let resolveDialog = null;
let resolveFolderPicker = null;
let pickerSnapshot = null;
let deletedRuleIds = [];

function receiveBridgeMessage(message) {
  let response;
  try { response = typeof message === 'string' ? JSON.parse(message) : message; } catch { return; }
  const pending = state.pending.get(response.id);
  if (!pending) return;
  state.pending.delete(response.id);
  if (response.ok) pending.resolve(response.result);
  else {
    const error = new Error(response.error);
    error.code = response.code;
    error.details = response.details;
    pending.reject(error);
  }
}

window.external.receiveMessage(receiveBridgeMessage);

function invoke(action, payload = {}) {
  return new Promise((resolve, reject) => {
    const id = crypto.randomUUID();
    state.pending.set(id, { resolve, reject });
    window.external.sendMessage(JSON.stringify({ id, action, payload }));
  });
}

async function refresh(action, payload, options = {}) {
  try {
    const previousSelection = options.preserveSelection ? new Set(state.selectedPaths) : null;
    closeContextMenu();
    document.querySelector('#error').hidden = true;
    state.snapshot = await invoke(action, payload);
    state.home ??= state.snapshot.path;
    if (previousSelection) {
      const available = new Set(state.snapshot.entries.map(entry => entry.fullPath));
      state.selectedPaths = new Set([...previousSelection].filter(path => available.has(path)));
    } else clearSelection();
    render();
  } catch (error) {
    const alert = document.querySelector('#error');
    alert.textContent = error.message;
    alert.hidden = false;
  }
}

function render() {
  const { path, entries, canGoBack, canGoForward, colors } = state.snapshot;
  state.snapshot.isSearch ? renderSearchHeading() : renderBreadcrumbs(path);
  document.querySelector('#back').disabled = !canGoBack;
  document.querySelector('#forward').disabled = !canGoForward;
  entriesElement.className = `entries ${state.view}`;
  const visibleEntries = entries.filter(entry => (!state.colorFilter || entry.colorId === state.colorFilter) && (!state.query || entry.name.toLocaleLowerCase().includes(state.query.toLocaleLowerCase())));
  entriesElement.replaceChildren(...visibleEntries.map(entry => {
    const index = entries.indexOf(entry);
    const button = document.createElement('button');
    button.className = `entry${state.selectedPaths.has(entry.fullPath) ? ' selected' : ''}`;
    button.title = entry.fullPath;
    button.innerHTML = `<span class="icon">${entry.isDirectory ? '📁' : '📄'}</span><span class="name"></span><span class="kind">${entry.isDirectory ? 'Folder' : formatSize(entry.size)}</span><span class="modified">${new Date(entry.modifiedDate).toLocaleDateString()}</span><span class="tag"></span>`;
    button.querySelector('.name').textContent = entry.name;
    const assignedColor = colors.find(color => color.id === entry.colorId);
    const tag = button.querySelector('.tag');
    tag.style.setProperty('--tag', assignedColor?.hexColor ?? 'transparent');
    tag.title = assignedColor ? `${assignedColor.name} tag${entry.assignmentType === 'automatic' ? ' (automatic)' : ''}` : '';
    button.onclick = event => selectEntry(event, entry, index);
    button.oncontextmenu = event => {
      event.preventDefault();
      event.stopPropagation();
      if (!state.selectedPaths.has(entry.fullPath)) {
        state.selectedPaths.clear();
        state.selectedPaths.add(entry.fullPath);
        state.anchorIndex = index;
        render();
      }
      showContextMenu(event.clientX, event.clientY, false);
    };
    button.ondblclick = () => entry.isDirectory
      ? refresh('navigation.open', { path: entry.fullPath })
      : invoke('filesystem.open', { path: entry.fullPath }).catch(showError);
    return button;
  }), ...(visibleEntries.length ? [] : [emptyState()]));
  const colorButtons = colors.map(color => {
    const button = document.createElement('button');
    button.className = 'swatch'; button.title = color.name;
    button.style.setProperty('--swatch', color.hexColor);
    button.onclick = () => refresh('color.assign', { paths: [...state.selectedPaths], color: color.id }, { preserveSelection:true });
    return button;
  });
  document.querySelector('#context-colors').replaceChildren(...colorButtons);
  renderColorFilters(colors);
}

function emptyState() {
  const element = document.createElement('div'); element.className = 'empty-state';
  element.textContent = state.snapshot.entries.length ? 'No items match these filters.' : 'No items to show.';
  return element;
}

function renderSearchHeading() {
  const container = document.querySelector('#path');
  const label = document.createElement('span'); label.className = 'breadcrumb'; label.textContent = state.snapshot.path;
  container.replaceChildren(label);
}

function renderColorFilters(colors) {
  const all = document.createElement('button'); all.className = `color-filter${state.colorFilter ? '' : ' active'}`; all.textContent = 'All items';
  all.onclick = () => { state.colorFilter = null; render(); };
  const buttons = colors.map(color => {
    const button = document.createElement('button'); button.className = `color-filter${state.colorFilter === color.id ? ' active' : ''}`;
    button.innerHTML = '<span class="color-dot"></span><span></span>'; button.firstElementChild.style.setProperty('--swatch', color.hexColor); button.lastElementChild.textContent = color.name;
    button.onclick = () => { state.colorFilter = state.colorFilter === color.id ? null : color.id; render(); };
    return button;
  });
  document.querySelector('#color-filters').replaceChildren(all, ...buttons);
}

function renderBreadcrumbs(path) {
  const container = document.querySelector('#path');
  const separator = path.includes('\\') ? '\\' : '/';
  const parts = path.split(/[\\/]+/).filter(Boolean);
  const crumbs = [];
  let current = separator === '/' ? '/' : '';
  if (separator === '/') crumbs.push({ label: '/', path: '/' });
  for (const part of parts) {
    current = current === '/' ? `/${part}` : current ? `${current}${separator}${part}` : part;
    crumbs.push({ label: part, path: current });
  }
  container.replaceChildren(...crumbs.map(crumb => {
    const button = document.createElement('button');
    button.className = 'breadcrumb';
    button.textContent = crumb.label;
    button.title = crumb.path;
    button.onclick = () => refresh('navigation.open', { path: crumb.path });
    return button;
  }));
  container.scrollLeft = container.scrollWidth;
}

function selectEntry(event, entry, index) {
  const additive = event.ctrlKey || event.metaKey;
  if (event.shiftKey && state.anchorIndex >= 0) {
    if (!additive) state.selectedPaths.clear();
    const start = Math.min(state.anchorIndex, index);
    const end = Math.max(state.anchorIndex, index);
    for (let position = start; position <= end; position++)
      state.selectedPaths.add(state.snapshot.entries[position].fullPath);
  } else if (additive) {
    state.selectedPaths.has(entry.fullPath)
      ? state.selectedPaths.delete(entry.fullPath)
      : state.selectedPaths.add(entry.fullPath);
    state.anchorIndex = index;
  } else {
    state.selectedPaths.clear();
    state.selectedPaths.add(entry.fullPath);
    state.anchorIndex = index;
  }
  render();
}

function clearSelection() {
  state.selectedPaths.clear();
  state.anchorIndex = -1;
}

function showContextMenu(x, y, emptySpace) {
  const count = state.selectedPaths.size;
  document.querySelector('#context-open').hidden = emptySpace;
  document.querySelector('#context-rename').hidden = emptySpace;
  document.querySelector('#context-duplicate').hidden = emptySpace;
  document.querySelector('#context-copy').hidden = emptySpace;
  document.querySelector('#context-move').hidden = emptySpace;
  document.querySelector('#context-item-options').hidden = emptySpace;
  document.querySelector('#context-new-folder').hidden = !emptySpace;
  document.querySelector('#context-open').disabled = count !== 1;
  document.querySelector('#context-rename').disabled = count !== 1;
  document.querySelector('#context-duplicate').disabled = count !== 1;
  contextMenu.hidden = false;
  const bounds = contextMenu.getBoundingClientRect();
  contextMenu.style.left = `${Math.max(6, Math.min(x, window.innerWidth - bounds.width - 6))}px`;
  contextMenu.style.top = `${Math.max(6, Math.min(y, window.innerHeight - bounds.height - 6))}px`;
  contextMenu.querySelector('button:not([hidden]):not(:disabled)')?.focus();
}

function closeContextMenu() {
  contextMenu.hidden = true;
}

function showError(error) {
  const alert = document.querySelector('#error');
  alert.textContent = error.message;
  alert.hidden = false;
}

function selectedEntries() {
  return state.snapshot.entries.filter(entry => state.selectedPaths.has(entry.fullPath));
}

async function createFolder() {
  const name = await askForText('New Folder', 'Enter a name for the new folder.');
  if (name?.trim()) await refresh('filesystem.createDirectory', { name });
}

async function renameSelected() {
  const selected = selectedEntries();
  if (selected.length !== 1) return;
  const name = await askForText('Rename', `Enter a new name for “${selected[0].name}”.`, selected[0].name, 'Rename');
  if (name?.trim() && name !== selected[0].name)
    await refresh('filesystem.rename', { path: selected[0].fullPath, name });
}

async function duplicateSelected() {
  const selected = selectedEntries();
  if (selected.length !== 1) return;
  await refresh('filesystem.duplicate', { path: selected[0].fullPath });
}

async function transferSelected(action) {
  const verb = action === 'filesystem.copy' ? 'Copy' : 'Move';
  const destination = await chooseFolder(`${verb} ${selectionDescription()} To`, state.snapshot.path);
  if (!destination) return;
  const sources = [...state.selectedPaths];
  for (const source of sources) {
    let name = null;
    while (true) {
      try {
        await invoke(action, { paths:[source], destination, ...(name ? { name } : {}) });
        break;
      } catch (error) {
        if (error.code !== 'destinationExists') throw error;
        name = await askForText(
          `${verb} with a New Name`,
          `“${error.details.name}” already exists in that folder. Enter a different name.`,
          error.details.name,
          verb);
        if (!name) return;
      }
    }
  }
  await refresh('filesystem.listDirectory', { path:state.snapshot.path });
}

async function trashSelected() {
  const count = state.selectedPaths.size;
  if (!count || !await askForConfirmation('Move to Trash', `Move ${selectionDescription()} to Trash?`, 'Move to Trash')) return;
  await refresh('filesystem.trash', { paths: [...state.selectedPaths] });
}

async function assignShortcutColor(colorId) {
  if (state.selectedPaths.size)
    await refresh('color.assign', { paths: [...state.selectedPaths], color: colorId }, { preserveSelection:true });
}

function selectionDescription() {
  const selected = selectedEntries();
  return selected.length === 1 ? `“${selected[0].name}”` : `${selected.length} selected items`;
}

function showDialog({ title, message, value = null, confirmLabel = 'OK', danger = false }) {
  if (resolveDialog) finishDialog(null);
  document.querySelector('#dialog-title').textContent = title;
  document.querySelector('#dialog-message').textContent = message;
  const confirmButton = document.querySelector('#dialog-confirm');
  confirmButton.textContent = confirmLabel;
  confirmButton.classList.toggle('danger', danger);
  dialogInput.hidden = value === null;
  dialogInput.required = value !== null;
  dialogInput.value = value ?? '';
  appDialog.showModal();
  value === null ? confirmButton.focus() : (dialogInput.focus(), dialogInput.select());
  return new Promise(resolve => { resolveDialog = resolve; });
}

function finishDialog(value) {
  const resolve = resolveDialog;
  resolveDialog = null;
  appDialog.close();
  resolve?.(value);
}

function askForText(title, message, value = '', confirmLabel = 'Create') {
  return showDialog({ title, message, value, confirmLabel });
}

function askForConfirmation(title, message, confirmLabel) {
  return showDialog({ title, message, confirmLabel, danger:true });
}

function chooseFolder(title, initialPath) {
  document.querySelector('#picker-title').textContent = title;
  folderPicker.showModal();
  return new Promise((resolve, reject) => {
    resolveFolderPicker = resolve;
    loadPickerDirectory(initialPath).catch(error => {
      finishFolderPicker(null);
      reject(error);
    });
  });
}

async function loadPickerDirectory(path) {
  pickerSnapshot = await invoke('filesystem.listDirectories', { path });
  document.querySelector('#picker-path').textContent = pickerSnapshot.path;
  document.querySelector('#picker-up').disabled = !pickerSnapshot.parent;
  const elements = pickerSnapshot.directories.map(directory => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'picker-folder';
    button.innerHTML = '<span>📁</span><span></span>';
    button.lastElementChild.textContent = directory.name;
    button.onclick = () => loadPickerDirectory(directory.fullPath).catch(showError);
    return button;
  });
  if (!elements.length) {
    const empty = document.createElement('div');
    empty.className = 'picker-empty';
    empty.textContent = 'No folders inside this location';
    elements.push(empty);
  }
  document.querySelector('#picker-folders').replaceChildren(...elements);
}

function finishFolderPicker(path) {
  const resolve = resolveFolderPicker;
  resolveFolderPicker = null;
  folderPicker.close();
  resolve?.(path);
}

async function openSelected() {
  for (const entry of selectedEntries()) {
    if (entry.isDirectory) {
      if (state.selectedPaths.size === 1) await refresh('navigation.open', { path: entry.fullPath });
    } else {
      await invoke('filesystem.open', { path: entry.fullPath });
    }
  }
  closeContextMenu();
}

function formatSize(bytes) {
  if (bytes == null) return '';
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB']; let value = bytes / 1024; let unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
  return `${value.toFixed(value < 10 ? 1 : 0)} ${units[unit]}`;
}

document.querySelector('#back').onclick = () => refresh('navigation.back');
document.querySelector('#forward').onclick = () => refresh('navigation.forward');
document.querySelector('#up').onclick = () => refresh('navigation.up');
document.querySelector('#home-link').onclick = () => refresh('navigation.open', { path: state.home });
document.querySelector('#search').oninput = event => { state.query = event.target.value; render(); };
document.querySelector('#search-colors').onclick = () => {
  if (!state.colorFilter) { showError(new Error('Choose a color first, then search.')); return; }
  refresh('color.search', { color:state.colorFilter, query:state.query });
};
const settingsDialog = document.querySelector('#settings-dialog');
document.querySelector('#color-settings').onclick = () => {
  deletedRuleIds = [];
  const rows = state.snapshot.colors.map(color => {
    const label = document.createElement('label'); label.className = 'settings-row';
    label.innerHTML = '<span class="color-dot"></span><input maxlength="32" required>';
    label.firstElementChild.style.setProperty('--swatch', color.hexColor); label.lastElementChild.value = color.name; label.lastElementChild.dataset.color = color.id;
    return label;
  });
  document.querySelector('#settings-colors').replaceChildren(...rows);
  document.querySelector('#rules-list').replaceChildren(...(state.snapshot.rules ?? []).map(createRuleRow));
  settingsDialog.showModal();
};
function createRuleRow(rule = { id:'', extension:'', colorId:'blue', enabled:true, priority:0 }) {
  const row = document.createElement('div'); row.className = 'rule-row'; row.dataset.id = rule.id;
  const options = state.snapshot.colors.map(color => `<option value="${color.id}"></option>`).join('');
  row.innerHTML = `<label class="rule-enabled-wrap" title="Enable this rule"><input class="rule-enabled" type="checkbox" aria-label="Enabled"></label><input class="rule-extension" placeholder=".pdf" required maxlength="32" aria-label="Extension"><select class="rule-color" aria-label="Color">${options}</select><input class="rule-priority" type="number" value="0" title="Higher priority wins when rules overlap" aria-label="Priority"><button class="rule-delete" type="button" title="Delete rule">×</button>`;
  row.querySelector('.rule-enabled').checked = rule.enabled; row.querySelector('.rule-extension').value = rule.extension; row.querySelector('.rule-color').value = rule.colorId; row.querySelector('.rule-priority').value = rule.priority;
  [...row.querySelector('.rule-color').options].forEach((option, index) => { option.textContent = state.snapshot.colors[index].name; });
  row.querySelector('.rule-delete').onclick = () => {
    if (row.dataset.id) deletedRuleIds.push(row.dataset.id);
    row.remove();
  };
  return row;
}
document.querySelector('#add-rule').onclick = () => document.querySelector('#rules-list').append(createRuleRow());
document.querySelector('#settings-cancel').onclick = () => settingsDialog.close();
document.querySelector('#settings-form').onsubmit = async event => {
  event.preventDefault();
  try {
    for (const input of document.querySelectorAll('#settings-colors input')) await invoke('color.rename', { color:input.dataset.color, name:input.value });
    for (const id of deletedRuleIds) await invoke('rules.delete', { id });
    for (const row of document.querySelectorAll('.rule-row')) await invoke('rules.save', { id:row.dataset.id, extension:row.querySelector('.rule-extension').value, color:row.querySelector('.rule-color').value, enabled:row.querySelector('.rule-enabled').checked, priority:Number(row.querySelector('.rule-priority').value) });
    settingsDialog.close(); await refresh('filesystem.listDirectory', { path: state.snapshot.isSearch ? state.home : state.snapshot.path });
  } catch (error) { showError(error); }
};
document.querySelector('#context-open').onclick = () => openSelected().catch(showError);
document.querySelector('#context-clear').onclick = () => refresh('color.remove', { paths: [...state.selectedPaths] }, { preserveSelection:true });
document.querySelector('#context-rename').onclick = () => renameSelected().catch(showError);
document.querySelector('#context-duplicate').onclick = () => duplicateSelected().catch(showError);
document.querySelector('#context-copy').onclick = () => transferSelected('filesystem.copy').catch(showError);
document.querySelector('#context-move').onclick = () => transferSelected('filesystem.move').catch(showError);
document.querySelector('#context-trash').onclick = () => trashSelected().catch(showError);
document.querySelector('#context-new-folder').onclick = () => createFolder().catch(showError);
document.querySelector('#dialog-form').onsubmit = event => {
  event.preventDefault();
  if (!dialogInput.hidden && !dialogInput.value.trim()) {
    dialogInput.setCustomValidity('A value is required.');
    dialogInput.reportValidity();
    return;
  }
  dialogInput.setCustomValidity('');
  finishDialog(dialogInput.hidden ? true : dialogInput.value.trim());
};
document.querySelector('#dialog-cancel').onclick = () => finishDialog(null);
appDialog.oncancel = event => { event.preventDefault(); finishDialog(null); };
document.querySelector('#picker-form').onsubmit = event => {
  event.preventDefault();
  finishFolderPicker(pickerSnapshot?.path ?? null);
};
document.querySelector('#picker-cancel').onclick = () => finishFolderPicker(null);
document.querySelector('#picker-up').onclick = () => {
  if (pickerSnapshot?.parent) loadPickerDirectory(pickerSnapshot.parent).catch(showError);
};
folderPicker.oncancel = event => { event.preventDefault(); finishFolderPicker(null); };
entriesElement.onclick = event => {
  if (event.target === entriesElement) { clearSelection(); closeContextMenu(); render(); }
};
entriesElement.oncontextmenu = event => {
  if (event.target !== entriesElement) return;
  event.preventDefault();
  clearSelection();
  render();
  showContextMenu(event.clientX, event.clientY, true);
};
document.addEventListener('pointerdown', event => {
  if (!contextMenu.hidden && !contextMenu.contains(event.target)) closeContextMenu();
});
document.addEventListener('keydown', event => {
  const modifier = event.ctrlKey || event.metaKey;
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a') {
    event.preventDefault();
    state.selectedPaths = new Set(state.snapshot.entries.map(entry => entry.fullPath));
    state.anchorIndex = state.snapshot.entries.length ? 0 : -1;
    render();
  } else if (event.key === 'Escape') {
    if (!contextMenu.hidden) closeContextMenu();
    else { clearSelection(); render(); }
  } else if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
    event.preventDefault();
    const selected = entriesElement.querySelector('.entry.selected');
    const bounds = selected?.getBoundingClientRect() ?? entriesElement.getBoundingClientRect();
    showContextMenu(bounds.left + 16, bounds.top + 16, !selected);
  } else if (event.key === 'F2' && state.selectedPaths.size === 1) {
    event.preventDefault();
    renameSelected().catch(showError);
  } else if (modifier && event.key.toLowerCase() === 'd' && state.selectedPaths.size === 1) {
    event.preventDefault();
    duplicateSelected().catch(showError);
  } else if (event.key === 'Delete' && state.selectedPaths.size) {
    event.preventDefault();
    trashSelected().catch(showError);
  } else if (modifier && event.shiftKey && event.key.toLowerCase() === 'n') {
    event.preventDefault();
    createFolder().catch(showError);
  } else if (modifier && /^[0-6]$/.test(event.key) && state.selectedPaths.size) {
    event.preventDefault();
    const colors = { '1':'red', '2':'orange', '3':'yellow', '4':'green', '5':'blue', '6':'purple' };
    event.key === '0'
      ? refresh('color.remove', { paths:[...state.selectedPaths] }, { preserveSelection:true })
      : assignShortcutColor(colors[event.key]);
  }
});
for (const view of ['grid', 'list']) document.querySelector(`#${view}-view`).onclick = () => {
  state.view = view;
  document.querySelector('#grid-view').classList.toggle('active', view === 'grid');
  document.querySelector('#list-view').classList.toggle('active', view === 'list');
  render();
};

refresh('app.initialize');
