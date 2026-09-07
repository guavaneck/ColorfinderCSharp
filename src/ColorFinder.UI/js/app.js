const state = { snapshot: null, selected: null, home: null, view: 'grid', pending: new Map() };
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
    state.selected = null;
    render();
  } catch (error) {
    const alert = document.querySelector('#error');
    alert.textContent = error.message;
    alert.hidden = false;
  }
}

function render() {
  const { path, entries, canGoBack, canGoForward, colors } = state.snapshot;
  document.querySelector('#path').textContent = path;
  document.querySelector('#back').disabled = !canGoBack;
  document.querySelector('#forward').disabled = !canGoForward;
  entriesElement.className = `entries ${state.view}`;
  entriesElement.replaceChildren(...entries.map(entry => {
    const button = document.createElement('button');
    button.className = `entry${state.selected?.fullPath === entry.fullPath ? ' selected' : ''}`;
    button.title = entry.fullPath;
    button.innerHTML = `<span class="icon">${entry.isDirectory ? '📁' : '📄'}</span><span class="name"></span><span class="kind">${entry.isDirectory ? 'Folder' : formatSize(entry.size)}</span><span class="modified">${new Date(entry.modifiedDate).toLocaleDateString()}</span><span class="tag"></span>`;
    button.querySelector('.name').textContent = entry.name;
    const assignedColor = colors.find(color => color.id === entry.colorId);
    const tag = button.querySelector('.tag');
    tag.style.setProperty('--tag', assignedColor?.hexColor ?? 'transparent');
    tag.title = assignedColor ? `${assignedColor.name} tag` : '';
    button.onclick = () => { state.selected = entry; render(); };
    button.ondblclick = () => entry.isDirectory && refresh('navigation.open', { path: entry.fullPath });
    return button;
  }));
  const bar = document.querySelector('#color-bar');
  bar.hidden = !state.selected;
  document.querySelector('#selection-label').textContent = state.selected?.name ?? 'No selection';
  const colorButtons = colors.map(color => {
    const button = document.createElement('button');
    button.className = 'swatch'; button.title = color.name;
    button.style.setProperty('--swatch', color.hexColor);
    button.onclick = () => refresh('color.assign', { path: state.selected.fullPath, color: color.id });
    return button;
  });
  document.querySelector('#colors').replaceChildren(...colorButtons);
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
document.querySelector('#clear-color').onclick = () => refresh('color.remove', { path: state.selected.fullPath });
for (const view of ['grid', 'list']) document.querySelector(`#${view}-view`).onclick = () => {
  state.view = view;
  document.querySelector('#grid-view').classList.toggle('active', view === 'grid');
  document.querySelector('#list-view').classList.toggle('active', view === 'list');
  render();
};

refresh('app.initialize');
