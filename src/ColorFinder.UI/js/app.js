const state = { snapshot: null, selectedPaths: new Set(), anchorIndex: -1, home: null, view: 'grid', pending: new Map() };
const entriesElement = document.querySelector('#entries');

function receiveBridgeMessage(message) {
  let response;
  try { response = typeof message === 'string' ? JSON.parse(message) : message; } catch { return; }
  const pending = state.pending.get(response.id);
  if (!pending) return;
  state.pending.delete(response.id);
  response.ok ? pending.resolve(response.result) : pending.reject(new Error(response.error));
}

window.external.receiveMessage(receiveBridgeMessage);

function invoke(action, payload = {}) {
  return new Promise((resolve, reject) => {
    const id = crypto.randomUUID();
    state.pending.set(id, { resolve, reject });
    window.external.sendMessage(JSON.stringify({ id, action, payload }));
  });
}

async function refresh(action, payload) {
  try {
    document.querySelector('#error').hidden = true;
    state.snapshot = await invoke(action, payload);
    state.home ??= state.snapshot.path;
    clearSelection();
    render();
  } catch (error) {
    const alert = document.querySelector('#error');
    alert.textContent = error.message;
    alert.hidden = false;
  }
}

function render() {
  const { path, entries, canGoBack, canGoForward, colors } = state.snapshot;
  renderBreadcrumbs(path);
  document.querySelector('#back').disabled = !canGoBack;
  document.querySelector('#forward').disabled = !canGoForward;
  entriesElement.className = `entries ${state.view}`;
  entriesElement.replaceChildren(...entries.map((entry, index) => {
    const button = document.createElement('button');
    button.className = `entry${state.selectedPaths.has(entry.fullPath) ? ' selected' : ''}`;
    button.title = entry.fullPath;
    button.innerHTML = `<span class="icon">${entry.isDirectory ? '📁' : '📄'}</span><span class="name"></span><span class="kind">${entry.isDirectory ? 'Folder' : formatSize(entry.size)}</span><span class="modified">${new Date(entry.modifiedDate).toLocaleDateString()}</span><span class="tag"></span>`;
    button.querySelector('.name').textContent = entry.name;
    const assignedColor = colors.find(color => color.id === entry.colorId);
    const tag = button.querySelector('.tag');
    tag.style.setProperty('--tag', assignedColor?.hexColor ?? 'transparent');
    tag.title = assignedColor ? `${assignedColor.name} tag` : '';
    button.onclick = event => selectEntry(event, entry, index);
    button.ondblclick = () => entry.isDirectory
      ? refresh('navigation.open', { path: entry.fullPath })
      : invoke('filesystem.open', { path: entry.fullPath }).catch(showError);
    return button;
  }));
  const bar = document.querySelector('#color-bar');
  bar.hidden = state.selectedPaths.size === 0;
  document.querySelector('#selection-label').textContent = selectionLabel();
  const colorButtons = colors.map(color => {
    const button = document.createElement('button');
    button.className = 'swatch'; button.title = color.name;
    button.style.setProperty('--swatch', color.hexColor);
    button.onclick = () => refresh('color.assign', { paths: [...state.selectedPaths], color: color.id });
    return button;
  });
  document.querySelector('#colors').replaceChildren(...colorButtons);
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

function selectionLabel() {
  if (state.selectedPaths.size === 0) return 'No selection';
  if (state.selectedPaths.size > 1) return `${state.selectedPaths.size} items selected`;
  return state.snapshot.entries.find(entry => state.selectedPaths.has(entry.fullPath))?.name ?? '1 item selected';
}

function showError(error) {
  const alert = document.querySelector('#error');
  alert.textContent = error.message;
  alert.hidden = false;
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
document.querySelector('#clear-color').onclick = () => refresh('color.remove', { paths: [...state.selectedPaths] });
entriesElement.onclick = event => {
  if (event.target === entriesElement) { clearSelection(); render(); }
};
document.addEventListener('keydown', event => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a') {
    event.preventDefault();
    state.selectedPaths = new Set(state.snapshot.entries.map(entry => entry.fullPath));
    state.anchorIndex = state.snapshot.entries.length ? 0 : -1;
    render();
  } else if (event.key === 'Escape') {
    clearSelection();
    render();
  }
});
for (const view of ['grid', 'list']) document.querySelector(`#${view}-view`).onclick = () => {
  state.view = view;
  document.querySelector('#grid-view').classList.toggle('active', view === 'grid');
  document.querySelector('#list-view').classList.toggle('active', view === 'list');
  render();
};

refresh('app.initialize');
