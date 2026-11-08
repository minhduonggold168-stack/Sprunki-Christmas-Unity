// Sprunki: Christmas Kids Wonderland - Web Engine
// Replicating Unity DSP AudioSyncManager, CharacterSlot, DraggableIcon & SprunkiStageController

(function () {
  'use strict';

  // --- Configuration & Constants ---
  const TOTAL_SLOTS = 7;
  const LOOP_DURATION = 9.6; // 2 bars = 8 beats at ~100 BPM (DSP synchronized)
  const BEAT_DURATION = LOOP_DURATION / 8.0; // 1.2s per main beat

  // Headpiece configs matching Unity CharacterSlot.ResolveHeadpieceForCharacter
  function getHeadpieceConfig(charId, slotIndex) {
    const id = (charId || '').toLowerCase();
    if (id.includes('sun')) {
      return { src: '../Assets/Art/UI/Christmas/SantaHat.png', top: '-18px', left: '56%', width: '54%', rotate: '-24deg' };
    } else if (id.includes('computer')) {
      return { src: '../Assets/Art/UI/Christmas/SantaHat.png', top: '-12px', left: '46%', width: '50%', rotate: '-18deg' };
    } else if (id.includes('brud') || id.includes('brown')) {
      return { src: '../Assets/Art/UI/Christmas/SantaHat.png', top: '-22px', left: '32%', width: '52%', rotate: '-10deg' };
    } else if (id.includes('raddy') || id.includes('durple') || id.includes('simon')) {
      return { src: '../Assets/Art/UI/Christmas/ReindeerAntlers.png', top: '-20px', left: '26%', width: '62%', rotate: '0deg' };
    } else if (id.includes('fun_bot') || id.includes('owakcx')) {
      return { src: '../Assets/Art/UI/Christmas/ElfHollyCap.png', top: '-16px', left: '36%', width: '52%', rotate: '-15deg' };
    } else {
      const src = (slotIndex % 2 === 0) ? '../Assets/Art/UI/Christmas/SantaHat.png' : '../Assets/Art/UI/Christmas/ElfHollyCap.png';
      return { src, top: '-14px', left: '32%', width: '52%', rotate: '-9deg' };
    }
  }

  // --- Audio Engine (Web Audio API) ---
  let audioCtx = null;
  const audioBuffers = new Map(); // url -> AudioBuffer
  let popBuffer = null;
  let loopStartTime = 0;
  let isAudioReady = false;

  function initAudioContext() {
    if (!audioCtx) {
      const AudioContextClass = window.AudioContext || window.webkitAudioContext;
      audioCtx = new AudioContextClass();
    }
    if (audioCtx.state === 'suspended') {
      audioCtx.resume();
    }
  }

  async function loadAudioBuffer(url) {
    if (!url) return null;
    if (audioBuffers.has(url)) return audioBuffers.get(url);

    try {
      const response = await fetch(url);
      const arrayBuffer = await response.arrayBuffer();
      const decoded = await audioCtx.decodeAudioData(arrayBuffer);
      audioBuffers.set(url, decoded);
      return decoded;
    } catch (e) {
      console.warn('Could not load audio:', url, e);
      return null;
    }
  }

  function playPopSfx() {
    if (!audioCtx || !popBuffer) return;
    try {
      const src = audioCtx.createBufferSource();
      src.buffer = popBuffer;
      const gain = audioCtx.createGain();
      gain.gain.value = 0.5;
      src.connect(gain);
      gain.connect(audioCtx.destination);
      src.start();
    } catch (e) {}
  }

  // --- State Variables ---
  const slotsState = []; // 7 slots
  for (let i = 0; i < TOTAL_SLOTS; i++) {
    slotsState.push({
      index: i,
      character: null,
      isMuted: false,
      isSolo: false,
      audioNode: null,
      gainNode: null,
      animTimer: null,
      animIndex: 0
    });
  }

  let isPartyMode = false;
  let isSnowstorm = false;
  let isAutoRandomRunning = false;
  let autoRandomRemainingSeconds = 120;
  let autoRandomTimerInterval = null;
  let autoRandomRoutineRunning = false;

  // Anti-duplication state (Lần sau khác lần trước 100%)
  let previousLineup = []; // Array of character objects
  const lastCharacterPerSlot = new Array(TOTAL_SLOTS).fill(null);

  // --- DOM Elements ---
  const stageSlotsContainer = document.getElementById('stage-slots');
  const iconTray = document.getElementById('icon-tray');
  const activeCountText = document.getElementById('active-count-text');
  const btnAutoRandom = document.getElementById('btn-auto-random');
  const btnSnowstorm = document.getElementById('btn-snowstorm');
  const btnSantaMagic = document.getElementById('btn-santa-magic');
  const btnReset = document.getElementById('btn-reset');
  const stageBackground = document.getElementById('stage-background');
  const auroraLayer = document.getElementById('aurora-layer');
  const ghostIcon = document.getElementById('ghost-icon');
  const ghostImg = document.getElementById('ghost-img');
  const ghostTag = document.getElementById('ghost-tag');
  const audioUnlockOverlay = document.getElementById('audio-unlock-overlay');
  const snowCanvas = document.getElementById('snow-canvas');

  // --- Snow Particles ---
  const snowCtx = snowCanvas.getContext('2d');
  let flakes = [];
  function resizeSnowCanvas() {
    snowCanvas.width = snowCanvas.offsetWidth;
    snowCanvas.height = snowCanvas.offsetHeight;
  }
  window.addEventListener('resize', resizeSnowCanvas);

  function initSnowFlakes() {
    flakes = [];
    const count = isSnowstorm ? 180 : 65;
    for (let i = 0; i < count; i++) {
      flakes.push({
        x: Math.random() * (snowCanvas.width || 1200),
        y: Math.random() * (snowCanvas.height || 600),
        r: Math.random() * 3.5 + 1.2,
        speed: Math.random() * 2 + 1,
        sway: Math.random() * 2 - 1
      });
    }
  }

  function renderSnow() {
    snowCtx.clearRect(0, 0, snowCanvas.width, snowCanvas.height);
    snowCtx.fillStyle = 'rgba(255, 255, 255, 0.85)';
    snowCtx.beginPath();
    const speedMult = isSnowstorm ? 2.5 : 1.0;
    for (let i = 0; i < flakes.length; i++) {
      const f = flakes[i];
      snowCtx.moveTo(f.x, f.y);
      snowCtx.arc(f.x, f.y, f.r, 0, Math.PI * 2, true);
      f.y += f.speed * speedMult;
      f.x += Math.sin(f.y * 0.02) * (isSnowstorm ? 2.5 : 0.8);
      if (f.y > snowCanvas.height) {
        f.y = -10;
        f.x = Math.random() * snowCanvas.width;
      }
    }
    snowCtx.fill();
    requestAnimationFrame(renderSnow);
  }

  // --- Build UI: 7 Slots & 20 Tray Icons ---
  function buildSlotsUI() {
    stageSlotsContainer.innerHTML = '';
    for (let i = 0; i < TOTAL_SLOTS; i++) {
      const slotEl = document.createElement('div');
      slotEl.className = 'char-slot';
      slotEl.id = `slot-${i}`;
      slotEl.dataset.slotIndex = i;

      // Glow Ring
      const glowRing = document.createElement('div');
      glowRing.className = 'slot-glow-ring';

      // Visual container
      const visual = document.createElement('div');
      visual.className = 'char-visual';

      // Sprite
      const sprite = document.createElement('img');
      sprite.className = 'char-sprite';
      sprite.src = '../Assets/Art/CharactersPNG/Polo_Base/polo_idle.png';

      // Hat Overlay
      const hat = document.createElement('img');
      hat.className = 'hat-overlay';
      const defaultHat = (i % 2 === 0) ? '../Assets/Art/UI/Christmas/SantaHat.png' : '../Assets/Art/UI/Christmas/ElfHollyCap.png';
      hat.src = defaultHat;
      hat.style.transform = (i % 2 === 0) ? 'rotate(-9deg)' : 'rotate(10deg)';

      // Mr. Tree Noel Overlay
      const treeOverlay = document.createElement('div');
      treeOverlay.className = 'mr-tree-overlay';
      treeOverlay.innerHTML = `
        <div class="noel-star"></div>
        <div class="noel-bauble" style="top: 22%; left: 35%; color: #e74c3c; background: #e74c3c;"></div>
        <div class="noel-bauble" style="top: 30%; left: 55%; color: #f1c40f; background: #f1c40f;"></div>
        <div class="noel-bauble" style="top: 45%; left: 28%; color: #3498db; background: #3498db;"></div>
        <div class="noel-bauble" style="top: 52%; left: 62%; color: #9b59b6; background: #9b59b6;"></div>
        <div class="noel-bauble" style="top: 60%; left: 45%; color: #e91e63; background: #e91e63;"></div>
      `;

      visual.appendChild(sprite);
      visual.appendChild(hat);
      visual.appendChild(treeOverlay);

      // Controls (M/S/X)
      const controls = document.createElement('div');
      controls.className = 'slot-controls';
      controls.innerHTML = `
        <div class="slot-ctrl-btn ctrl-mute" title="Mute">M</div>
        <div class="slot-ctrl-btn ctrl-solo" title="Solo">S</div>
        <div class="slot-ctrl-btn ctrl-remove" title="Remove">✕</div>
      `;

      controls.querySelector('.ctrl-mute').onclick = (e) => { e.stopPropagation(); toggleMute(i); };
      controls.querySelector('.ctrl-solo').onclick = (e) => { e.stopPropagation(); toggleSolo(i); };
      controls.querySelector('.ctrl-remove').onclick = (e) => { e.stopPropagation(); removeSlot(i); };

      slotEl.appendChild(glowRing);
      slotEl.appendChild(visual);
      slotEl.appendChild(controls);

      // Drag & Drop event handlers
      slotEl.ondragover = (e) => { e.preventDefault(); slotEl.classList.add('drag-over'); };
      slotEl.ondragleave = () => { slotEl.classList.remove('drag-over'); };
      slotEl.ondrop = (e) => {
        e.preventDefault();
        slotEl.classList.remove('drag-over');
        handleUserInteraction();
        const charOrder = parseInt(e.dataTransfer.getData('text/plain'));
        const char = SPRUNKI_CHARACTERS.find(c => c.order === charOrder);
        if (char) activateSlot(i, char);
      };

      slotEl.onclick = () => {
        handleUserInteraction();
      };

      stageSlotsContainer.appendChild(slotEl);
    }
  }

  function buildTrayUI() {
    iconTray.innerHTML = '';
    SPRUNKI_CHARACTERS.forEach(char => {
      const item = document.createElement('div');
      item.className = 'tray-item';
      item.id = `tray-item-${char.order}`;
      item.draggable = true;

      const img = document.createElement('img');
      img.src = '../' + char.icon;
      img.alt = char.displayName;
      item.appendChild(img);

      item.ondragstart = (e) => {
        handleUserInteraction();
        e.dataTransfer.setData('text/plain', char.order);
      };

      // Click to place in first available slot
      item.onclick = () => {
        handleUserInteraction();
        const emptyIdx = slotsState.findIndex(s => !s.character);
        if (emptyIdx !== -1) {
          activateSlot(emptyIdx, char);
        } else {
          // Replace slot 0
          activateSlot(0, char);
        }
      };

      iconTray.appendChild(item);
    });
  }

  // --- Slot Activation & Audio Sync ---
  function activateSlot(slotIndex, character) {
    const slot = slotsState[slotIndex];
    const slotEl = document.getElementById(`slot-${slotIndex}`);
    if (!slot || !slotEl) return;

    slot.character = character;
    lastCharacterPerSlot[slotIndex] = character;
    slotEl.classList.add('active');

    playPopSfx();

    const visual = slotEl.querySelector('.char-visual');
    const sprite = slotEl.querySelector('.char-sprite');
    const hat = slotEl.querySelector('.hat-overlay');
    const treeOverlay = slotEl.querySelector('.mr-tree-overlay');

    const isMrTree = character.id.includes('tree');

    if (isMrTree) {
      hat.style.display = 'none';
      treeOverlay.style.display = 'block';
    } else {
      treeOverlay.style.display = 'none';
      hat.style.display = 'block';
      const headCfg = getHeadpieceConfig(character.id, slotIndex);
      hat.src = headCfg.src;
      hat.style.top = headCfg.top;
      hat.style.left = headCfg.left;
      hat.style.width = headCfg.width;
      hat.style.transform = `rotate(${headCfg.rotate})`;
    }

    // Set sprite to idle
    sprite.src = '../' + character.idle;

    // Start audio loop synced to master loop clock
    startSlotAudio(slotIndex);

    // Start singing animation cycle
    startSlotAnimation(slotIndex);

    updateActiveCountDisplay();
  }

  function removeSlot(slotIndex) {
    const slot = slotsState[slotIndex];
    const slotEl = document.getElementById(`slot-${slotIndex}`);
    if (!slot || !slotEl) return;

    playPopSfx();
    stopSlotAudio(slotIndex);
    stopSlotAnimation(slotIndex);

    slot.character = null;
    slotEl.classList.remove('active');

    const sprite = slotEl.querySelector('.char-sprite');
    const hat = slotEl.querySelector('.hat-overlay');
    const treeOverlay = slotEl.querySelector('.mr-tree-overlay');

    sprite.src = '../Assets/Art/CharactersPNG/Polo_Base/polo_idle.png';
    treeOverlay.style.display = 'none';
    hat.style.display = 'block';
    const defaultHat = (slotIndex % 2 === 0) ? '../Assets/Art/UI/Christmas/SantaHat.png' : '../Assets/Art/UI/Christmas/ElfHollyCap.png';
    hat.src = defaultHat;
    hat.style.top = '-14px';
    hat.style.left = '32%';
    hat.style.width = '52%';
    hat.style.transform = (slotIndex % 2 === 0) ? 'rotate(-9deg)' : 'rotate(10deg)';

    updateActiveCountDisplay();
  }

  // --- Audio Sync Scheduling ---
  async function startSlotAudio(slotIndex) {
    if (!audioCtx) return;
    const slot = slotsState[slotIndex];
    if (!slot || !slot.character) return;

    const audioUrl = (isPartyMode && slot.character.partyAudio)
      ? '../' + slot.character.partyAudio
      : '../' + slot.character.audio;

    if (!audioUrl) return;

    stopSlotAudio(slotIndex);

    const buffer = await loadAudioBuffer(audioUrl);
    if (!buffer || !slot.character) return;

    const src = audioCtx.createBufferSource();
    src.buffer = buffer;
    src.loop = true;

    const gain = audioCtx.createGain();
    gain.gain.value = slot.isMuted ? 0 : (isSnowstorm ? 1.25 : 1.0);

    src.connect(gain);
    gain.connect(audioCtx.destination);

    // Calculate synchronized start offset
    const now = audioCtx.currentTime;
    if (loopStartTime === 0) {
      loopStartTime = now;
    }
    const elapsed = (now - loopStartTime) % LOOP_DURATION;
    const offset = elapsed % buffer.duration;

    src.start(0, offset);

    slot.audioNode = src;
    slot.gainNode = gain;
  }

  function stopSlotAudio(slotIndex) {
    const slot = slotsState[slotIndex];
    if (slot && slot.audioNode) {
      try {
        slot.audioNode.stop();
        slot.audioNode.disconnect();
      } catch (e) {}
      slot.audioNode = null;
      slot.gainNode = null;
    }
  }

  function refreshAllSlotAudio() {
    for (let i = 0; i < TOTAL_SLOTS; i++) {
      if (slotsState[i].character) {
        startSlotAudio(i);
      }
    }
  }

  // --- Character Singing Animation ---
  function startSlotAnimation(slotIndex) {
    const slot = slotsState[slotIndex];
    const slotEl = document.getElementById(`slot-${slotIndex}`);
    if (!slot || !slot.character || !slotEl) return;

    stopSlotAnimation(slotIndex);

    const sprite = slotEl.querySelector('.char-sprite');
    const visual = slotEl.querySelector('.char-visual');
    const hat = slotEl.querySelector('.hat-overlay');
    const frames = slot.character.frames;
    if (!frames || frames.length === 0) return;

    let frameIdx = 0;
    slot.animTimer = setInterval(() => {
      if (!slot.character) return;
      frameIdx = (frameIdx + 1) % frames.length;
      sprite.src = '../' + frames[frameIdx];

      // Bounce character slightly on beat
      const bounce = Math.sin(Date.now() * 0.01 + slotIndex) * 4;
      visual.style.transform = `translateY(${bounce}px)`;
    }, 110);
  }

  function stopSlotAnimation(slotIndex) {
    const slot = slotsState[slotIndex];
    const slotEl = document.getElementById(`slot-${slotIndex}`);
    if (slot && slot.animTimer) {
      clearInterval(slot.animTimer);
      slot.animTimer = null;
    }
    if (slotEl) {
      const visual = slotEl.querySelector('.char-visual');
      if (visual) visual.style.transform = 'translateY(0)';
    }
  }

  // --- Mute / Solo ---
  function toggleMute(slotIndex) {
    handleUserInteraction();
    const slot = slotsState[slotIndex];
    const slotEl = document.getElementById(`slot-${slotIndex}`);
    if (!slot || !slot.character) return;

    slot.isMuted = !slot.isMuted;
    if (slot.gainNode) {
      slot.gainNode.gain.value = slot.isMuted ? 0 : 1.0;
    }
    const btn = slotEl.querySelector('.ctrl-mute');
    btn.classList.toggle('active', slot.isMuted);
  }

  function toggleSolo(slotIndex) {
    handleUserInteraction();
    const slot = slotsState[slotIndex];
    const slotEl = document.getElementById(`slot-${slotIndex}`);
    if (!slot || !slot.character) return;

    slot.isSolo = !slot.isSolo;
    const anySolo = slotsState.some(s => s.isSolo);

    slotsState.forEach((s, idx) => {
      const el = document.getElementById(`slot-${idx}`);
      if (s.gainNode) {
        if (anySolo) {
          s.gainNode.gain.value = s.isSolo ? 1.0 : 0;
        } else {
          s.gainNode.gain.value = s.isMuted ? 0 : 1.0;
        }
      }
      if (el) {
        el.querySelector('.ctrl-solo').classList.toggle('active', s.isSolo);
      }
    });
  }

  // --- Global Controls ---
  function updateActiveCountDisplay() {
    const activeCount = slotsState.filter(s => s.character).length;
    if (isAutoRandomRunning) {
      const m = Math.floor(Math.max(0, autoRandomRemainingSeconds) / 60);
      const s = Math.floor(Math.max(0, autoRandomRemainingSeconds) % 60);
      const mm = String(m).padStart(2, '0');
      const ss = String(s).padStart(2, '0');
      activeCountText.innerText = `🎲 Auto (${mm}:${ss}) • ${activeCount}/7`;
    } else if (isPartyMode) {
      activeCountText.innerText = `${activeCount}/7 SANTA MAGIC!`;
    } else {
      activeCountText.innerText = `${activeCount}/7 Singers`;
    }
  }

  function resetEntireStage() {
    handleUserInteraction();
    for (let i = 0; i < TOTAL_SLOTS; i++) {
      if (slotsState[i].character) {
        removeSlot(i);
      }
    }
    loopStartTime = audioCtx ? audioCtx.currentTime : 0;
    updateActiveCountDisplay();
  }

  function togglePartyMode() {
    handleUserInteraction();
    isPartyMode = !isPartyMode;
    btnSantaMagic.classList.toggle('active', isPartyMode);
    stageBackground.classList.toggle('party-mode', isPartyMode);
    auroraLayer.classList.toggle('party-mode', isPartyMode);
    refreshAllSlotAudio();
    updateActiveCountDisplay();
  }

  function toggleSnowstorm() {
    handleUserInteraction();
    isSnowstorm = !isSnowstorm;
    btnSnowstorm.classList.toggle('active', isSnowstorm);
    btnSnowstorm.innerText = isSnowstorm ? 'Blizzard ON' : 'Snowstorm';
    initSnowFlakes();
    refreshAllSlotAudio();
  }

  // --- Beat Visualizer LEDs Loop ---
  function updateBeatVisualizer() {
    if (audioCtx && loopStartTime > 0) {
      const elapsed = (audioCtx.currentTime - loopStartTime) % LOOP_DURATION;
      const currentBeat = Math.floor((elapsed / LOOP_DURATION) * 8);

      for (let i = 0; i < 8; i++) {
        const led = document.getElementById(`led-${i}`);
        if (led) {
          if (i === currentBeat) {
            led.classList.add('active');
            led.classList.toggle('party', isPartyMode);
          } else {
            led.classList.remove('active', 'party');
          }
        }
      }
    }
    requestAnimationFrame(updateBeatVisualizer);
  }

  // --- Auto Random 2-Minute DJ Showcase Engine ---
  function toggleAutoRandom2Min() {
    playPopSfx();
    if (isAutoRandomRunning) {
      stopAutoRandomInternal();
    } else {
      startAutoRandom2Min();
    }
  }

  function startAutoRandom2Min() {
    stopAutoRandomInternal();
    isAutoRandomRunning = true;
    autoRandomRemainingSeconds = 120;
    btnAutoRandom.classList.add('running');
    btnAutoRandom.innerText = '⏹ Auto Random (02:00)';
    updateActiveCountDisplay();

    // 120-second countdown interval
    autoRandomTimerInterval = setInterval(() => {
      autoRandomRemainingSeconds--;
      if (autoRandomRemainingSeconds <= 0) {
        autoRandomRemainingSeconds = 0;
        stopAutoRandomInternal();
      } else {
        const m = Math.floor(autoRandomRemainingSeconds / 60);
        const s = autoRandomRemainingSeconds % 60;
        const mm = String(m).padStart(2, '0');
        const ss = String(s).padStart(2, '0');
        btnAutoRandom.innerText = `⏹ Auto Random (${mm}:${ss})`;
        updateActiveCountDisplay();
      }
    }, 1000);

    runAutoRandomRoutine();
  }

  function stopAutoRandomInternal() {
    isAutoRandomRunning = false;
    autoRandomRemainingSeconds = 120;
    if (autoRandomTimerInterval) {
      clearInterval(autoRandomTimerInterval);
      autoRandomTimerInterval = null;
    }
    btnAutoRandom.classList.remove('running');
    btnAutoRandom.innerText = '▶ Auto Random (02:00)';
    ghostIcon.style.display = 'none';
    updateActiveCountDisplay();
  }

  function handleUserInteraction() {
    if (isAutoRandomRunning) {
      stopAutoRandomInternal();
    }
  }

  // Anti-duplication Character Picker
  function pickAntiDuplicateCharacter(slotIndex, currentWaveUsed, preferredCategory) {
    const lastForSlot = lastCharacterPerSlot[slotIndex];

    // 1. Eligible pool: not already placed on stage in this wave, not equal to lastForSlot
    const eligible = SPRUNKI_CHARACTERS.filter(c => {
      if (currentWaveUsed.has(c)) return false;
      if (lastForSlot && c.order === lastForSlot.order) return false;
      return true;
    });

    if (eligible.length === 0) {
      // Fallback
      return SPRUNKI_CHARACTERS.find(c => !currentWaveUsed.has(c)) || SPRUNKI_CHARACTERS[0];
    }

    // 2. Partition into Fresh Group A (not in previousLineup) and Repeat Group B
    const freshGroup = eligible.filter(c => !previousLineup.some(p => p.order === c.order));
    const candidateGroup = freshGroup.length > 0 ? freshGroup : eligible;

    // 3. Category match
    const categoryMatches = candidateGroup.filter(c => c.category === preferredCategory);
    if (categoryMatches.length > 0) {
      return categoryMatches[Math.floor(Math.random() * categoryMatches.length)];
    }

    return candidateGroup[Math.floor(Math.random() * candidateGroup.length)];
  }

  // Ghost Icon Bezier Flight Animation
  function animateGhostFlight(character, targetSlotIndex, durationMs = 520) {
    return new Promise(resolve => {
      const trayItem = document.getElementById(`tray-item-${character.order}`);
      const targetSlot = document.getElementById(`slot-${targetSlotIndex}`);
      if (!trayItem || !targetSlot) {
        resolve();
        return;
      }

      const trayRect = trayItem.getBoundingClientRect();
      const slotRect = targetSlot.getBoundingClientRect();

      const startX = trayRect.left + trayRect.width / 2;
      const startY = trayRect.top + trayRect.height / 2;
      const endX = slotRect.left + slotRect.width / 2;
      const endY = slotRect.top + slotRect.height * 0.45;

      const midX = (startX + endX) / 2 + (Math.random() * 80 - 40);
      const controlY = Math.min(startY, endY) - 100;

      ghostImg.src = '../' + character.icon;
      ghostIcon.style.display = 'flex';
      ghostTag.innerText = `🎲 AUTO #${character.order}`;

      const startTime = performance.now();

      function step(now) {
        if (!isAutoRandomRunning) {
          ghostIcon.style.display = 'none';
          resolve();
          return;
        }

        const elapsed = now - startTime;
        const t = Math.min(1, elapsed / durationMs);
        const ease = t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t;

        // Quadratic Bezier
        const curX = (1 - ease) * (1 - ease) * startX + 2 * (1 - ease) * ease * midX + ease * ease * endX;
        const curY = (1 - ease) * (1 - ease) * startY + 2 * (1 - ease) * ease * controlY + ease * ease * endY;

        ghostIcon.style.left = curX + 'px';
        ghostIcon.style.top = curY + 'px';

        if (t < 1) {
          requestAnimationFrame(step);
        } else {
          ghostIcon.style.display = 'none';
          resolve();
        }
      }
      requestAnimationFrame(step);
    });
  }

  const delay = (ms) => new Promise(res => setTimeout(res, ms));

  async function runAutoRandomRoutine() {
    if (autoRandomRoutineRunning) return;
    autoRandomRoutineRunning = true;

    // Reset stage first
    for (let i = 0; i < TOTAL_SLOTS; i++) {
      if (slotsState[i].character) removeSlot(i);
    }
    await delay(200);

    const slotCategories = [0, 0, 1, 2, 2, 3, 3]; // Beats, Effects, Melodies, Voices
    let waveNumber = 1;

    // --- Wave 1: Initial 7-character assembly ---
    const currentLineup = [];
    const currentWaveUsed = new Set();

    for (let s = 0; s < TOTAL_SLOTS; s++) {
      if (!isAutoRandomRunning) { autoRandomRoutineRunning = false; return; }

      const chosen = pickAntiDuplicateCharacter(s, currentWaveUsed, slotCategories[s]);
      currentWaveUsed.add(chosen);
      currentLineup.push(chosen);

      await animateGhostFlight(chosen, s, 500);
      if (!isAutoRandomRunning) { autoRandomRoutineRunning = false; return; }

      activateSlot(s, chosen);
      await delay(s === 0 ? 80 : 950);
    }

    previousLineup = [...currentLineup];

    // --- Continuous 120-Second Loop ---
    while (isAutoRandomRunning && autoRandomRemainingSeconds > 0) {
      waveNumber++;

      // Let the full groove play for ~10-12s
      const playWait = 11000;
      const startTime = Date.now();
      while (Date.now() - startTime < playWait) {
        if (!isAutoRandomRunning || autoRandomRemainingSeconds <= 0) {
          autoRandomRoutineRunning = false;
          return;
        }
        await delay(200);
      }

      if (!isAutoRandomRunning || autoRandomRemainingSeconds <= 0) break;

      // If less than 8 seconds left, play out to the end
      if (autoRandomRemainingSeconds < 8) {
        while (isAutoRandomRunning && autoRandomRemainingSeconds > 0) {
          await delay(200);
        }
        break;
      }

      // Alternate: Odd waves partial swap (3 slots), Even waves full breakdown & rebuild
      const doFullRefresh = (waveNumber % 2 === 0);

      if (doFullRefresh) {
        // Full breakdown
        const breakdownOrder = [6, 5, 4, 2, 1, 3, 0];
        for (const idx of breakdownOrder) {
          if (!isAutoRandomRunning) { autoRandomRoutineRunning = false; return; }
          removeSlot(idx);
          await delay(180);
        }
        await delay(300);

        currentLineup.length = 0;
        currentWaveUsed.clear();

        for (let s = 0; s < TOTAL_SLOTS; s++) {
          if (!isAutoRandomRunning) { autoRandomRoutineRunning = false; return; }

          const chosen = pickAntiDuplicateCharacter(s, currentWaveUsed, slotCategories[s]);
          currentWaveUsed.add(chosen);
          currentLineup.push(chosen);

          await animateGhostFlight(chosen, s, 480);
          if (!isAutoRandomRunning) { autoRandomRoutineRunning = false; return; }

          activateSlot(s, chosen);
          await delay(s === 0 ? 80 : 950);
        }

        previousLineup = [...currentLineup];
      } else {
        // Partial swap: 3 slots
        const slotsToSwap = (waveNumber % 4 === 1) ? [2, 4, 6] : [1, 3, 5];

        for (const s of slotsToSwap) {
          if (!isAutoRandomRunning) { autoRandomRoutineRunning = false; return; }
          removeSlot(s);
          await delay(200);
        }
        await delay(250);

        for (const s of slotsToSwap) {
          if (!isAutoRandomRunning) { autoRandomRoutineRunning = false; return; }

          const activeSet = new Set(slotsState.map(sl => sl.character).filter(Boolean));
          const chosen = pickAntiDuplicateCharacter(s, activeSet, slotCategories[s]);

          await animateGhostFlight(chosen, s, 480);
          if (!isAutoRandomRunning) { autoRandomRoutineRunning = false; return; }

          activateSlot(s, chosen);
          await delay(900);
        }

        previousLineup = slotsState.map(sl => sl.character).filter(Boolean);
      }
    }

    stopAutoRandomInternal();
    autoRandomRoutineRunning = false;
  }

  // --- Start & Audio Unlock ---
  async function unlockAudio() {
    initAudioContext();
    popBuffer = await loadAudioBuffer('../Assets/GameAssets/Audio/UI/pop.wav');
    playPopSfx();
    audioUnlockOverlay.style.display = 'none';
    isAudioReady = true;
    resizeSnowCanvas();
    initSnowFlakes();
  }

  audioUnlockOverlay.onclick = unlockAudio;

  // Header button listeners
  btnAutoRandom.onclick = toggleAutoRandom2Min;
  btnSnowstorm.onclick = toggleSnowstorm;
  btnSantaMagic.onclick = togglePartyMode;
  btnReset.onclick = resetEntireStage;

  // Initialize
  buildSlotsUI();
  buildTrayUI();
  initSnowFlakes();
  renderSnow();
  updateBeatVisualizer();
})();
