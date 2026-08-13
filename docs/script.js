const root = document.documentElement;
const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

const presets = {
  void: { surface: '#04080e', card: '#0d141f', text: '#f4f8ff', muted: '#91a1b6', cyan: '#48dcf9', magenta: '#ff4fd8', mint: '#58e6b2', radius: 24, gap: 4, opacity: 88 },
  aurora: { surface: '#0e0718', card: '#171025', text: '#faf5ff', muted: '#bea9d0', cyan: '#56e2ff', magenta: '#ff5bd7', mint: '#5bf1be', radius: 30, gap: 6, opacity: 84 },
  slate: { surface: '#111821', card: '#182331', text: '#f7fbff', muted: '#b3c3d4', cyan: '#59d7ef', magenta: '#e66acb', mint: '#62dfb4', radius: 18, gap: 5, opacity: 94 },
  ember: { surface: '#160d0a', card: '#241510', text: '#fff8f3', muted: '#ceb3a6', cyan: '#4dd7ef', magenta: '#ff5db3', mint: '#5ee1a8', radius: 20, gap: 5, opacity: 92 },
  contrast: { surface: '#000000', card: '#080b10', text: '#ffffff', muted: '#d6e0ee', cyan: '#62e7ff', magenta: '#ff5be2', mint: '#62f4bb', radius: 12, gap: 5, opacity: 100 },
  ghost: { surface: '#e8f0f6', card: '#fafdff', text: '#0c1722', muted: '#425466', cyan: '#007e9a', magenta: '#8e4ec6', mint: '#008f74', radius: 28, gap: 6, opacity: 94 },
  terminal: { surface: '#030f08', card: '#07190d', text: '#e8fff0', muted: '#8ac9a0', cyan: '#5cff9d', magenta: '#ff7bdd', mint: '#5cff9d', radius: 8, gap: 3, opacity: 90 },
  frameless: { surface: '#05080d', card: '#0a1018', text: '#f5f9ff', muted: '#9aabc0', cyan: '#62d7ff', magenta: '#ff60d7', mint: '#5be6b2', radius: 18, gap: 2, opacity: 72 }
};

const designerWidget = document.querySelector('#designer-widget');
const presetLabel = document.querySelector('#preset-label');
let activePresetName = 'void';
const controls = {
  opacity: document.querySelector('#opacity-control'),
  radius: document.querySelector('#radius-control'),
  gap: document.querySelector('#gap-control'),
  cpu: document.querySelector('#cpu-color'),
  gpu: document.querySelector('#gpu-color'),
  ram: document.querySelector('#ram-color')
};

function renderPreset(name) {
  const preset = presets[name];
  if (!preset || !designerWidget) return;
  activePresetName = name;
  designerWidget.style.opacity = '1';
  designerWidget.style.setProperty('--surface-preview', preset.surface);
  designerWidget.style.background = `color-mix(in srgb, ${preset.surface} ${preset.opacity}%, transparent)`;
  designerWidget.style.color = preset.text;
  designerWidget.style.borderRadius = `${preset.radius}px`;
  designerWidget.style.setProperty('--gap', `${preset.gap}px`);
  designerWidget.querySelectorAll('.designer-module').forEach(item => {
    item.style.background = `color-mix(in srgb, ${preset.card} 78%, transparent)`;
    item.style.borderRadius = `${Math.max(3, Math.round(preset.radius * .38))}px`;
  });
  designerWidget.querySelectorAll('.designer-module b').forEach(item => item.style.color = preset.text);
  designerWidget.querySelectorAll('.designer-module small').forEach(item => item.style.color = preset.muted);
  designerWidget.querySelector('.cpu').style.color = preset.cyan;
  designerWidget.querySelector('.gpu').style.color = preset.magenta;
  designerWidget.querySelector('.ram').style.color = preset.mint;
  designerWidget.querySelector('.net').style.color = preset.cyan;
  presetLabel.textContent = name.toUpperCase();
  controls.opacity.value = preset.opacity;
  controls.radius.value = preset.radius;
  controls.gap.value = preset.gap;
  controls.cpu.value = preset.cyan;
  controls.gpu.value = preset.magenta;
  controls.ram.value = preset.mint;
  updateOutputs();
}

function updateOutputs() {
  document.querySelector('#opacity-output').textContent = `${controls.opacity.value}%`;
  document.querySelector('#radius-output').textContent = `${controls.radius.value} px`;
  document.querySelector('#gap-output').textContent = `${controls.gap.value} px`;
}

document.querySelectorAll('[data-preset]').forEach(button => {
  button.addEventListener('click', () => {
    document.querySelectorAll('[data-preset]').forEach(item => {
      const active = item === button;
      item.classList.toggle('active', active);
      item.setAttribute('aria-pressed', String(active));
    });
    renderPreset(button.dataset.preset);
  });
});

controls.opacity?.addEventListener('input', () => {
  const surface = presets[activePresetName].surface;
  designerWidget.style.background = `color-mix(in srgb, ${surface} ${controls.opacity.value}%, transparent)`;
  updateOutputs();
});
controls.radius?.addEventListener('input', () => {
  designerWidget.style.borderRadius = `${controls.radius.value}px`;
  designerWidget.querySelectorAll('.designer-module').forEach(item => item.style.borderRadius = `${Math.max(2, Math.round(controls.radius.value * .38))}px`);
  updateOutputs();
});
controls.gap?.addEventListener('input', () => { designerWidget.style.setProperty('--gap', `${controls.gap.value}px`); updateOutputs(); });
controls.cpu?.addEventListener('input', () => { designerWidget.querySelector('.cpu').style.color = controls.cpu.value; designerWidget.querySelector('.net').style.color = controls.cpu.value; });
controls.gpu?.addEventListener('input', () => { designerWidget.querySelector('.gpu').style.color = controls.gpu.value; });
controls.ram?.addEventListener('input', () => { designerWidget.querySelector('.ram').style.color = controls.ram.value; });

document.querySelectorAll('[data-toggle]').forEach(button => {
  button.addEventListener('click', () => {
    const active = !button.classList.contains('active');
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
    designerWidget.classList.toggle(`no-${button.dataset.toggle}`, !active);
  });
});

const layoutSpecs = { pill: 'PILL // 196 × 350', rail: 'RAIL // 240 × 286', dock: 'DOCK // 700 × 104', mini: 'MINI // 176 × 204' };
document.querySelectorAll('[data-layout]').forEach(button => {
  button.addEventListener('click', () => {
    const layout = button.dataset.layout;
    const preview = document.querySelector('#layout-preview');
    preview.className = `layout-preview layout-${layout}`;
    document.querySelector('#layout-name').textContent = layoutSpecs[layout];
    document.querySelectorAll('[data-layout]').forEach(item => {
      const active = item === button;
      item.classList.toggle('active', active);
      item.setAttribute('aria-pressed', String(active));
    });
  });
});

const observer = new IntersectionObserver(entries => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
      observer.unobserve(entry.target);
    }
  });
}, { threshold: .08 });
document.querySelectorAll('.reveal').forEach(element => reduceMotion ? element.classList.add('visible') : observer.observe(element));

const progress = document.querySelector('#signal-progress');
function updateProgress() {
  const scrollable = document.documentElement.scrollHeight - window.innerHeight;
  progress.style.width = `${scrollable > 0 ? (window.scrollY / scrollable) * 100 : 0}%`;
}
window.addEventListener('scroll', updateProgress, { passive: true });
updateProgress();

const dialog = document.querySelector('#image-dialog');
const dialogImage = dialog?.querySelector('img');
document.querySelectorAll('[data-dialog-image]').forEach(button => {
  button.addEventListener('click', () => {
    dialogImage.src = button.dataset.dialogImage;
    dialog.showModal();
  });
});
document.querySelector('#dialog-close')?.addEventListener('click', () => dialog.close());
dialog?.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });

document.querySelector('#copy-build')?.addEventListener('click', async event => {
  try {
    await navigator.clipboard.writeText('dotnet build native/OpsMonitor.slnx -c Release');
    event.currentTarget.textContent = 'COPIED';
    window.setTimeout(() => { event.currentTarget.textContent = 'COPY'; }, 1500);
  } catch {
    event.currentTarget.textContent = 'SELECT';
  }
});

renderPreset('void');
