/*
 * SeatGuard booking console.
 *
 * Talks to the same-origin API exactly as any other client would: no privileged
 * back channel. The only admin operation (event creation) sends X-Api-Key.
 */

const API = '/api/v1';

const ICONS = {
  calendar: '<rect x="3" y="5" width="18" height="16" rx="3"/><path d="M8 3v4M16 3v4M3 10h18"/>',
  pin: '<path d="M12 21s7-5.6 7-11a7 7 0 1 0-14 0c0 5.4 7 11 7 11z"/><circle cx="12" cy="10" r="2.5"/>',
  users: '<path d="M16 20v-1.5a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4V20"/><circle cx="9.5" cy="7" r="3.5"/><path d="M17 11a3 3 0 1 0 0-6M21 20v-1a4 4 0 0 0-3-3.8"/>',
  ticket: '<path d="M4 8a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v1a2.5 2.5 0 0 0 0 5v1a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-1a2.5 2.5 0 0 0 0-5V8z"/><path d="M13 6v12" stroke-dasharray="2 3"/>',
  check: '<circle cx="12" cy="12" r="9"/><path d="m8.5 12.5 2.5 2.5 4.5-5"/>',
  alert: '<path d="M12 3 2.5 20h19L12 3z"/><path d="M12 9v5M12 17.5v.01"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8v.01"/>',
};

const els = {
  eventSelect: document.getElementById('event-select'),
  eventMeta: document.getElementById('event-meta'),
  refreshBtn: document.getElementById('refresh-btn'),
  newEventBtn: document.getElementById('new-event-btn'),
  autoRefresh: document.getElementById('auto-refresh'),
  updatedAt: document.getElementById('updated-at'),
  seatMap: document.getElementById('seat-map'),
  seatSummary: document.getElementById('seat-summary'),
  statAvailable: document.getElementById('stat-available'),
  statHeld: document.getElementById('stat-held'),
  statSold: document.getElementById('stat-sold'),
  statTotal: document.getElementById('stat-total'),
  selectedSeat: document.getElementById('selected-seat'),
  customerId: document.getElementById('customer-id'),
  regenerateCustomer: document.getElementById('regenerate-customer'),
  useIdempotency: document.getElementById('use-idempotency'),
  reserveBtn: document.getElementById('reserve-btn'),
  replayBtn: document.getElementById('replay-btn'),
  bookingHint: document.getElementById('booking-hint'),
  reservationPanel: document.getElementById('reservation-panel'),
  raceCount: document.getElementById('race-count'),
  raceBtn: document.getElementById('race-btn'),
  raceHint: document.getElementById('race-hint'),
  raceResults: document.getElementById('race-results'),
  requestLog: document.getElementById('request-log'),
  clearLog: document.getElementById('clear-log'),
  apiStatus: document.getElementById('api-status'),
  apiStatusText: document.getElementById('api-status-text'),
  toasts: document.getElementById('toasts'),
  dialog: document.getElementById('event-dialog'),
  eventForm: document.getElementById('event-form'),
  closeDialog: document.getElementById('close-dialog'),
  cancelEvent: document.getElementById('cancel-event'),
  submitEvent: document.getElementById('submit-event'),
  eventName: document.getElementById('event-name'),
  eventVenue: document.getElementById('event-venue'),
  eventDate: document.getElementById('event-date'),
  apiKey: document.getElementById('api-key'),
  sectionList: document.getElementById('section-list'),
  addSection: document.getElementById('add-section'),
  eventError: document.getElementById('event-error'),
};

const state = {
  events: [],
  eventId: null,
  eventDetails: null,
  availability: null,
  selectedSeatId: null,
  reservation: null,
  lastHold: null,
  busy: false,
  raceRunning: false,
  race: null,
  autoRefresh: true,
  log: [],
  logSeq: 0,
  lastUpdated: null,
  expiryRefresh: false,
};

const moneyFormatters = new Map();

class ApiError extends Error {
  constructor(result) {
    const problem = result.problem;
    super(result.network
      ? 'Network request failed'
      : (problem?.detail || problem?.title || `HTTP ${result.status}`));
    this.name = 'ApiError';
    this.status = result.status;
    this.code = problem?.code;
    this.problem = problem;
    this.network = Boolean(result.network);
  }
}

// ---------------------------------------------------------------- utilities

function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);

  for (const [key, value] of Object.entries(props)) {
    if (value === null || value === undefined || value === false) continue;

    if (key === 'class') node.className = value;
    else if (key === 'text') node.textContent = value;
    else if (key === 'html') node.innerHTML = value;
    else if (key === 'style') node.setAttribute('style', value);
    else if (key === 'dataset') Object.assign(node.dataset, value);
    else if (key === 'attrs') {
      for (const [name, attrValue] of Object.entries(value)) {
        if (attrValue !== null && attrValue !== undefined) node.setAttribute(name, attrValue);
      }
    } else if (key.startsWith('on') && typeof value === 'function') {
      node.addEventListener(key.slice(2).toLowerCase(), value);
    } else if (key in node) {
      node[key] = value;
    } else {
      node.setAttribute(key, value === true ? '' : value);
    }
  }

  for (const child of children.flat()) {
    if (child === null || child === undefined || child === false) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }

  return node;
}

function icon(name) {
  return `<svg viewBox="0 0 24 24" aria-hidden="true">${ICONS[name] ?? ''}</svg>`;
}

function metaItem(iconName, text) {
  const item = el('span', { class: 'meta-item', html: icon(iconName) });
  item.append(el('span', { text }));
  return item;
}

function uuid() {
  if (crypto?.randomUUID) return crypto.randomUUID();

  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (char) => {
    const random = (Math.random() * 16) | 0;
    return (char === 'x' ? random : (random & 0x3) | 0x8).toString(16);
  });
}

function generateCustomerId() {
  return `customer-${Math.random().toString(36).slice(2, 8)}`;
}

function shortId(value) {
  return value ? String(value).slice(0, 8) : '—';
}

function money(amount, currency) {
  if (amount === null || amount === undefined) return '—';

  const key = currency || 'EUR';
  let formatter = moneyFormatters.get(key);

  if (!formatter) {
    try {
      formatter = new Intl.NumberFormat('en-US', { style: 'currency', currency: key });
    } catch {
      formatter = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2 });
    }
    moneyFormatters.set(key, formatter);
  }

  return formatter.format(amount);
}

function formatDateTime(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';

  return new Intl.DateTimeFormat('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(date);
}

function formatClock(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';

  return new Intl.DateTimeFormat('en-US', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(date);
}

function formatDuration(milliseconds) {
  const totalSeconds = Math.max(0, Math.ceil(milliseconds / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;

  if (minutes >= 60) {
    return `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, '0')}m`;
  }

  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function storageGet(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}

function storageSet(key, value) {
  try { localStorage.setItem(key, value); } catch { /* private mode */ }
}

function seconds(milliseconds) {
  return `${(milliseconds / 1000).toFixed(milliseconds < 100 ? 2 : 1)}s`;
}

// ---------------------------------------------------------------- transport

async function apiFetch(path, { method = 'GET', body, headers = {}, log = true } = {}) {
  const requestHeaders = { Accept: 'application/json', ...headers };
  const init = { method, headers: requestHeaders };

  if (body !== undefined) {
    requestHeaders['Content-Type'] = 'application/json';
    init.body = JSON.stringify(body);
  }

  const started = performance.now();
  let response;

  try {
    response = await fetch(path, init);
  } catch (error) {
    const ms = Math.round(performance.now() - started);
    if (log) pushLog({ method, path, status: 0, ms, detail: String(error) });
    return { ok: false, status: 0, data: null, ms, problem: null, network: true };
  }

  const ms = Math.round(performance.now() - started);
  const text = await response.text();
  let data = null;

  if (text) {
    try { data = JSON.parse(text); } catch { data = text; }
  }

  if (log) {
    pushLog({
      method,
      path,
      status: response.status,
      ms,
      detail: data?.detail || data?.title || data?.code || null,
    });
  }

  return {
    ok: response.ok,
    status: response.status,
    data,
    ms,
    problem: response.ok ? null : data,
    network: false,
  };
}

async function request(path, options) {
  const result = await apiFetch(path, options);
  if (!result.ok) throw new ApiError(result);
  return result.data;
}

function logPath(path) {
  return path.replace(API, '').replace('/api/v1', '');
}

function pushLog(entry) {
  state.logSeq += 1;
  state.log.unshift({ seq: state.logSeq, at: new Date(), ...entry });
  state.log = state.log.slice(0, 60);
  renderLog();
}

function renderLog() {
  if (state.log.length === 0) {
    els.requestLog.replaceChildren(el('div', { class: 'empty-state small', text: 'Requests appear here.' }));
    return;
  }

  const rows = state.log.map((entry) => {
    const statusText = entry.status === 0 ? 'ERR' : String(entry.status);
    const statusClass = entry.status === 0 || entry.status >= 500
      ? 'err'
      : entry.status >= 400
        ? 'warn'
        : 'ok';

    const row = el('div', { class: 'log-row', attrs: { title: entry.detail || '' } },
      el('span', { class: 'time', text: formatClock(entry.at) }),
      el('span', { class: 'path', text: `${entry.method} ${logPath(entry.path)}` }),
      el('span', { class: `log-status ${statusClass}`, text: statusText }),
      el('span', { class: 'ms', text: `${entry.ms}ms` }));

    return row;
  });

  els.requestLog.replaceChildren(...rows);
}

// ---------------------------------------------------------------- toasts

function toast(message, type = 'info', timeout = 4800) {
  const iconName = type === 'error' ? 'alert' : type === 'success' ? 'check' : 'info';
  const node = el('div', { class: `toast ${type}`, attrs: { role: type === 'error' ? 'alert' : 'status' }, html: icon(iconName) });
  const text = el('span', { class: 'toast-text', text: message });
  const close = el('button', {
    class: 'toast-close',
    type: 'button',
    text: '✕',
    attrs: { 'aria-label': 'Dismiss notification' },
    onclick: () => node.remove(),
  });

  node.append(text, close);
  els.toasts.append(node);

  setTimeout(() => node.remove(), type === 'error' ? timeout + 2500 : timeout);
}

const ERROR_MESSAGES = {
  'seat.not_available': 'That seat is no longer available.',
  'seat.sold': 'That seat is already sold.',
  'reservation.expired': 'This hold has expired.',
  'reservation.invalid_state': 'The reservation does not allow this action in its current state.',
  'event.not_found': 'Event not found.',
  'reservation.not_found': 'Reservation not found.',
  'validation.failed': 'The request did not pass validation.',
};

function handleError(error, context) {
  if (error instanceof ApiError) {
    const friendly = ERROR_MESSAGES[error.code];
    const suffix = error.code ? ` (${error.code})` : '';
    toast(`${context}: ${friendly ?? error.message}${friendly ? suffix : ''}`, 'error');

    if (error.status === 409 || error.status === 410) {
      void refreshReservation({ quiet: true });
    }

    if (error.status === 409) {
      void refreshAvailability({ quiet: true });
    }
  } else {
    toast(`${context}: ${error.message || error}`, 'error');
  }
}

// ---------------------------------------------------------------- api status

async function checkHealth() {
  try {
    const response = await fetch('/health/live', { cache: 'no-store' });
    setApiStatus(response.ok ? 'ok' : 'down');
  } catch {
    setApiStatus('down');
  }
}

function setApiStatus(status) {
  els.apiStatus.dataset.state = status;
  els.apiStatusText.textContent = status === 'ok'
    ? 'API online'
    : status === 'down'
      ? 'API unreachable'
      : 'Connecting…';
}

// ---------------------------------------------------------------- events

async function loadEvents(preferredId = null) {
  try {
    const data = await request(`${API}/events?page=1&pageSize=50`);
    state.events = data.items ?? [];
    renderEventOptions();

    if (state.events.length === 0) {
      clearEventView();
      return;
    }

    const stored = preferredId || storageGet('seatguard.eventId');
    const eventId = state.events.some((item) => item.id === stored)
      ? stored
      : state.events[0].id;

    await selectEvent(eventId);
  } catch (error) {
    handleError(error, 'Loading events');
    setApiStatus('down');
  }
}

function renderEventOptions() {
  if (state.events.length === 0) {
    els.eventSelect.replaceChildren(el('option', { value: '', text: 'No events yet' }));
    els.eventSelect.disabled = true;
    return;
  }

  els.eventSelect.disabled = false;
  els.eventSelect.replaceChildren(
    ...state.events.map((item) => el('option', {
      value: item.id,
      text: `${item.name} — ${formatDateTime(item.startsAt)}`,
    })),
  );
}

function clearEventView() {
  state.eventId = null;
  state.eventDetails = null;
  state.availability = null;
  state.selectedSeatId = null;
  state.reservation = null;
  state.race = null;

  els.eventMeta.replaceChildren(el('span', { class: 'muted', text: 'Create an event to start booking.' }));
  els.seatSummary.textContent = 'No events yet.';
  els.seatMap.replaceChildren(el('div', {
    class: 'empty-state',
    text: 'The database is empty. Create an event to generate seats.',
  }));
  renderStats();
  renderSelectedSeat();
  renderReservation();
  renderRaceResults();
  syncControls();
}

async function selectEvent(eventId) {
  if (!eventId) return;

  state.eventId = eventId;
  state.eventDetails = null;
  state.availability = null;
  state.selectedSeatId = null;
  state.reservation = null;
  state.lastHold = null;
  state.race = null;
  state.expiryRefresh = false;
  state.lastUpdated = null;
  els.replayBtn.hidden = true;
  els.eventSelect.value = eventId;
  storageSet('seatguard.eventId', eventId);

  renderEventMeta();
  renderSeatMap();
  renderStats();
  renderSelectedSeat();
  renderReservation();
  renderRaceResults();
  syncControls();

  const requestedId = eventId;

  try {
    const [details, availability] = await Promise.all([
      request(`${API}/events/${eventId}`),
      request(`${API}/events/${eventId}/availability`),
    ]);

    if (state.eventId !== requestedId) return;

    state.eventDetails = details;
    state.availability = availability;
    state.lastUpdated = new Date();

    renderEventMeta();
    renderSeatMap();
    renderStats();
    renderUpdatedAt();
    renderSelectedSeat();
    syncControls();
  } catch (error) {
    if (state.eventId === requestedId) handleError(error, 'Loading event');
  }
}

function renderEventMeta() {
  const summary = state.events.find((item) => item.id === state.eventId);
  const details = state.eventDetails;

  if (!summary && !details) {
    els.eventMeta.replaceChildren(el('span', { class: 'muted', text: 'Select an event.' }));
    return;
  }

  const name = details?.name ?? summary.name;
  const venue = details?.venue ?? summary.venue;
  const startsAt = details?.startsAt ?? summary.startsAt;
  const seatCount = details?.seatCount ?? summary.seatCount;
  const status = details?.status ?? summary.status;

  els.eventMeta.replaceChildren(
    el('div', { class: 'event-name', text: name }),
    el('div', { class: 'event-meta-row' },
      metaItem('pin', venue),
      metaItem('calendar', formatDateTime(startsAt)),
      metaItem('users', `${seatCount.toLocaleString('en-US')} seats`),
      el('span', { class: 'pill confirmed', text: status })),
  );
}

// ---------------------------------------------------------------- seat map

function selectedSeat() {
  return state.availability?.seats.find((seat) => seat.id === state.selectedSeatId) ?? null;
}

function renderSeatMap() {
  if (!state.eventId) return;

  if (!state.availability) {
    els.seatMap.replaceChildren(el('div', { class: 'loading-block', text: 'Loading availability…' }));
    els.seatSummary.textContent = 'Fetching seat map…';
    return;
  }

  const activeSeat = document.activeElement?.dataset?.seatId;
  const scrollLeft = els.seatMap.scrollLeft;
  const scrollTop = els.seatMap.scrollTop;
  const seats = state.availability.seats;

  if (seats.length === 0) {
    els.seatMap.replaceChildren(el('div', { class: 'empty-state', text: 'This event has no seats.' }));
    return;
  }

  const groups = new Map();

  for (const seat of seats) {
    let section = groups.get(seat.section);
    if (!section) {
      section = {
        name: seat.section,
        rows: new Map(),
        priceAmount: seat.priceAmount,
        currency: seat.currency,
      };
      groups.set(seat.section, section);
    }

    let row = section.rows.get(seat.row);
    if (!row) {
      row = [];
      section.rows.set(seat.row, row);
    }
    row.push(seat);
  }

  const sectionOrder = [];
  for (const section of state.eventDetails?.sections ?? []) {
    if (groups.has(section.name)) sectionOrder.push(section.name);
  }
  for (const name of groups.keys()) {
    if (!sectionOrder.includes(name)) sectionOrder.push(name);
  }

  const nodes = sectionOrder.map((name) => {
    const group = groups.get(name);
    const detail = state.eventDetails?.sections.find((section) => section.name === name);
    const available = seats.filter((seat) => seat.section === name && seat.status === 'Available').length;
    const total = seats.filter((seat) => seat.section === name).length;

    const rowNodes = [...group.rows.entries()]
      .sort(([a], [b]) => a.localeCompare(b, 'en', { numeric: true }))
      .map(([rowName, rowSeats]) => {
        const seatButtons = rowSeats
          .slice()
          .sort((a, b) => a.number - b.number)
          .map((seat) => {
            const isSelected = seat.id === state.selectedSeatId;
            return el('button', {
              class: `seat ${seat.status.toLowerCase()}${isSelected ? ' selected' : ''}`,
              type: 'button',
              text: String(seat.number),
              title: `${seat.label} · ${money(seat.priceAmount, seat.currency)} · ${seat.status}`,
              attrs: {
                'aria-label': `${seat.label}, ${seat.status}, ${money(seat.priceAmount, seat.currency)}`,
                'aria-pressed': isSelected ? 'true' : 'false',
              },
              dataset: { seatId: seat.id },
            });
          });

        return el('div', { class: 'seat-row' },
          el('span', { class: 'row-label', text: rowName }),
          el('div', { class: 'seats' }, seatButtons));
      });

    return el('div', { class: 'section' },
      el('div', { class: 'section-head' },
        el('span', { class: 'section-name', text: name }),
        el('span', {
          class: 'section-price',
          text: detail ? money(detail.priceAmount, detail.currency) : money(group.priceAmount, group.currency),
        }),
        el('span', { class: 'section-count', text: `${available}/${total} free` })),
      el('div', { class: 'rows' }, rowNodes));
  });

  els.seatMap.replaceChildren(...nodes);
  els.seatMap.scrollLeft = scrollLeft;
  els.seatMap.scrollTop = scrollTop;

  if (activeSeat) {
    els.seatMap.querySelector(`[data-seat-id="${CSS.escape(activeSeat)}"]`)?.focus({ preventScroll: true });
  }

  els.seatSummary.textContent = `${seats.length.toLocaleString('en-US')} seats across ${sectionOrder.length} section${sectionOrder.length === 1 ? '' : 's'}`;
}

function renderStats() {
  const seats = state.availability?.seats ?? [];
  const count = (status) => seats.filter((seat) => seat.status === status).length;

  els.statAvailable.textContent = seats.length ? count('Available').toLocaleString('en-US') : '—';
  els.statHeld.textContent = seats.length ? count('Held').toLocaleString('en-US') : '—';
  els.statSold.textContent = seats.length ? count('Sold').toLocaleString('en-US') : '—';
  els.statTotal.textContent = seats.length ? seats.length.toLocaleString('en-US') : '—';
}

function renderUpdatedAt() {
  els.updatedAt.textContent = state.lastUpdated
    ? `Updated ${formatClock(state.lastUpdated)}`
    : '';
}

function selectSeat(seatId) {
  state.selectedSeatId = seatId;

  for (const button of els.seatMap.querySelectorAll('.seat.selected')) {
    button.classList.remove('selected');
    button.setAttribute('aria-pressed', 'false');
  }

  const button = els.seatMap.querySelector(`[data-seat-id="${CSS.escape(seatId)}"]`);
  if (button) {
    button.classList.add('selected');
    button.setAttribute('aria-pressed', 'true');
  }

  renderSelectedSeat();
}

function renderSelectedSeat() {
  const seat = selectedSeat();

  if (!seat) {
    els.selectedSeat.replaceChildren(el('div', {
      class: 'empty-state',
      text: 'Select a seat on the map.',
    }));
    syncControls();
    return;
  }

  const statusClass = seat.status === 'Available' ? 'confirmed' : seat.status === 'Held' ? 'pending' : 'cancelled';

  els.selectedSeat.replaceChildren(
    el('div', { class: 'selected-card' },
      el('div', { class: 'seat-badge', text: `${seat.row}${seat.number}` }),
      el('div', { class: 'selected-main' },
        el('div', { class: 'selected-label', text: seat.label }),
        el('div', { class: 'selected-meta' },
          el('span', { class: `pill ${statusClass}`, text: seat.status }),
          el('span', { class: 'selected-price', text: money(seat.priceAmount, seat.currency) }),
          el('span', { text: `Row ${seat.row} · Seat ${seat.number}` })))),
  );

  syncControls();
}

function syncControls() {
  const seat = selectedSeat();
  const hasPending = state.reservation?.status === 'Pending';

  els.reserveBtn.disabled = state.busy || !seat || hasPending;
  els.reserveBtn.setAttribute('aria-busy', state.busy ? 'true' : 'false');
  els.raceBtn.disabled = state.raceRunning || !seat || hasPending;
  els.refreshBtn.disabled = !state.eventId;

  if (hasPending) {
    showBookingHint('An active hold exists. Confirm or cancel it before creating another.', true);
  } else if (!seat) {
    hideBookingHint();
  } else if (seat.status === 'Sold') {
    showBookingHint('This seat is sold. A hold request will be rejected with 409 seat.not_available.', true);
  } else if (seat.status === 'Held') {
    showBookingHint('Held by another customer. Reserve it to observe the 409 seat.not_available conflict.', true);
  } else if (state.reservation?.status === 'Confirmed') {
    showBookingHint('Ticket issued. You can hold another seat for this customer.', false);
  } else {
    hideBookingHint();
  }

  if (state.raceRunning) {
    els.raceHint.textContent = `Firing ${state.race?.count ?? '…'} concurrent holds…`;
  } else if (!seat) {
    els.raceHint.textContent = 'Select a seat to start.';
  } else if (hasPending) {
    els.raceHint.textContent = 'Cancel or confirm the active hold first.';
  } else {
    els.raceHint.textContent = `Targets ${seat.label}. Every request uses a fresh idempotency key.`;
  }
}

function showBookingHint(text, dashed) {
  els.bookingHint.hidden = false;
  els.bookingHint.textContent = text;
  els.bookingHint.style.borderStyle = dashed ? 'dashed' : 'solid';
}

function hideBookingHint() {
  els.bookingHint.hidden = true;
}

// ---------------------------------------------------------------- booking

async function reserveSeat() {
  const seat = selectedSeat();
  if (!seat || state.busy) return;

  const customerId = els.customerId.value.trim() || generateCustomerId();
  els.customerId.value = customerId;
  storageSet('seatguard.customerId', customerId);

  const idempotencyKey = els.useIdempotency.checked ? uuid() : null;
  const headers = idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {};

  state.busy = true;
  syncControls();

  try {
    const reservation = await request(`${API}/reservations`, {
      method: 'POST',
      body: { eventId: state.eventId, seatId: seat.id, customerId },
      headers,
    });

    state.reservation = reservation;
    state.expiryRefresh = false;
    state.lastHold = idempotencyKey
      ? { eventId: state.eventId, seatId: seat.id, customerId, idempotencyKey, reservationId: reservation.id }
      : null;

    els.replayBtn.hidden = !state.lastHold;

    toast(`Seat ${seat.label} held for ${formatDuration(new Date(reservation.expiresAt) - Date.now())}.`, 'success');
    renderReservation();
    renderSelectedSeat();
    void refreshAvailability({ quiet: true });
  } catch (error) {
    handleError(error, 'Holding seat');
  } finally {
    state.busy = false;
    syncControls();
  }
}

async function replayHold() {
  if (!state.lastHold || state.busy) return;

  state.busy = true;
  syncControls();

  try {
    const result = await apiFetch(`${API}/reservations`, {
      method: 'POST',
      body: {
        eventId: state.lastHold.eventId,
        seatId: state.lastHold.seatId,
        customerId: state.lastHold.customerId,
      },
      headers: { 'Idempotency-Key': state.lastHold.idempotencyKey },
    });

    if (!result.ok) throw new ApiError(result);

    const same = result.data?.id === state.lastHold.reservationId;
    state.reservation = result.data;
    renderReservation();

    toast(
      same
        ? `Replay returned the same reservation (HTTP ${result.status}).`
        : `Replay returned a different reservation (HTTP ${result.status}).`,
      same ? 'success' : 'error',
    );
  } catch (error) {
    handleError(error, 'Replaying hold');
  } finally {
    state.busy = false;
    syncControls();
  }
}

async function confirmReservation() {
  if (!state.reservation || state.busy) return;

  state.busy = true;
  syncControls();

  try {
    const updated = await request(`${API}/reservations/${state.reservation.id}/confirm`, { method: 'POST' });
    state.reservation = updated;
    state.lastHold = null;
    els.replayBtn.hidden = true;
    toast(`Confirmed. Ticket ${updated.ticketCode}.`, 'success');
    renderReservation();
    renderSelectedSeat();
    void refreshAvailability({ quiet: true });
  } catch (error) {
    handleError(error, 'Confirming reservation');
  } finally {
    state.busy = false;
    syncControls();
  }
}

async function cancelReservation() {
  if (!state.reservation || state.busy) return;

  state.busy = true;
  syncControls();

  try {
    await request(`${API}/reservations/${state.reservation.id}/cancel`, { method: 'POST' });
    await refreshReservation({ quiet: true });
    toast('Hold released.', 'success');
    void refreshAvailability({ quiet: true });
  } catch (error) {
    handleError(error, 'Cancelling reservation');
  } finally {
    state.busy = false;
    syncControls();
  }
}

async function refreshReservation({ quiet = false } = {}) {
  const current = state.reservation;
  if (!current) return;

  try {
    const fresh = await request(`${API}/reservations/${current.id}`);
    state.reservation = fresh;
    renderReservation();
    renderSelectedSeat();

    if (fresh.status !== 'Pending') {
      state.expiryRefresh = false;
      els.replayBtn.hidden = true;
    } else if (new Date(fresh.expiresAt).getTime() <= Date.now()) {
      // The read model can lag the sweeper; retry until the status flips.
      setTimeout(() => { state.expiryRefresh = false; }, 3000);
    }
  } catch (error) {
    if (!quiet) handleError(error, 'Refreshing reservation');
  }
}

function renderReservation() {
  const reservation = state.reservation;
  els.reservationPanel.hidden = !reservation;

  if (!reservation) {
    els.reservationPanel.replaceChildren();
    syncControls();
    return;
  }

  const seat = state.availability?.seats.find((item) => item.id === reservation.seatId);
  const statusClass = reservation.status.toLowerCase();

  const header = el('div', { class: 'reservation-head' },
    el('h3', { text: 'Reservation' }),
    el('span', { class: 'pill ' + statusClass, text: reservation.status }),
    el('span', { class: 'spacer' }),
    el('span', { class: 'mono muted', text: shortId(reservation.id) }));

  const grid = el('dl', { class: 'reservation-grid' },
    kv('Seat', seat ? seat.label : shortId(reservation.seatId)),
    kv('Amount', money(reservation.amount, reservation.currency)),
    kv('Created', formatClock(reservation.createdAt)),
    kv('Expires', formatClock(reservation.expiresAt)),
    kv('Customer', reservation.customerId, true),
    kv('Reservation', reservation.id, true));

  const parts = [header, grid];

  if (reservation.status === 'Pending') {
    parts.push(el('div', { class: 'timer' },
      el('div', { class: 'timer-row' },
        el('span', { text: 'Hold expires in' }),
        el('span', { class: 'timer-value', id: 'timer-value', text: formatDuration(new Date(reservation.expiresAt) - Date.now()) })),
      el('div', { class: 'timer-track' },
        el('div', { class: 'timer-fill', id: 'timer-fill' }))));
  }

  if (reservation.status === 'Confirmed') {
    const ticket = el('div', { class: 'ticket', html: icon('ticket') });
    ticket.append(
      el('div', {},
        el('div', { class: 'ticket-label', text: 'Ticket code' }),
        el('div', { class: 'ticket-code', text: reservation.ticketCode || '—' })));

    parts.push(ticket);
  }

  if (reservation.status === 'Expired' || reservation.status === 'Cancelled') {
    parts.push(el('p', {
      class: 'hint',
      style: 'padding:12px 0 0',
      text: reservation.status === 'Expired'
        ? 'The sweeper expired this hold and released the seat back to inventory.'
        : 'This hold was released and the seat is available again.',
    }));
  }

  els.reservationPanel.replaceChildren(...parts);

  const actions = el('div', { class: 'reservation-actions' });

  if (reservation.status === 'Pending') {
    const confirm = el('button', {
      class: 'btn primary',
      type: 'button',
      disabled: state.busy,
      text: 'Confirm & pay',
      onclick: confirmReservation,
    });

    const cancel = el('button', {
      class: 'btn danger',
      type: 'button',
      disabled: state.busy,
      text: 'Cancel hold',
    });

    let armed = false;
    let disarmTimer = null;

    cancel.addEventListener('click', () => {
      if (!armed) {
        armed = true;
        cancel.textContent = 'Click again to cancel';
        cancel.classList.add('armed');
        disarmTimer = setTimeout(() => {
          armed = false;
          cancel.textContent = 'Cancel hold';
          cancel.classList.remove('armed');
        }, 4000);
        return;
      }

      clearTimeout(disarmTimer);
      void cancelReservation();
    });

    actions.append(confirm, cancel);
  } else if (reservation.status === 'Confirmed') {
    actions.append(
      el('button', {
        class: 'btn ghost',
        type: 'button',
        html: `${icon('ticket')}<span>Copy ticket</span>`,
        onclick: async () => {
          try {
            await navigator.clipboard.writeText(reservation.ticketCode || '');
            toast('Ticket code copied.', 'success');
          } catch {
            toast('Clipboard is not available.', 'error');
          }
        },
      }),
      el('button', {
        class: 'btn ghost',
        type: 'button',
        text: 'Dismiss',
        onclick: () => {
          state.reservation = null;
          renderReservation();
          renderSelectedSeat();
        },
      }));
  } else {
    actions.append(el('button', {
      class: 'btn ghost',
      type: 'button',
      text: 'Dismiss',
      onclick: () => {
        state.reservation = null;
        renderReservation();
        renderSelectedSeat();
      },
    }));
  }

  els.reservationPanel.append(actions);
  syncControls();

  if (reservation.status === 'Pending') tickReservation();
}

function kv(label, value, mono = false) {
  return el('div', { class: 'kv' },
    el('dt', { text: label }),
    el('dd', { class: mono ? 'mono' : '', text: value ?? '—', attrs: { title: value ?? '' } }));
}

function tickReservation() {
  const reservation = state.reservation;
  if (!reservation || reservation.status !== 'Pending') return;

  const value = document.getElementById('timer-value');
  const fill = document.getElementById('timer-fill');
  if (!value || !fill) return;

  const now = Date.now();
  const expiresAt = new Date(reservation.expiresAt).getTime();
  const createdAt = new Date(reservation.createdAt).getTime();
  const remaining = Math.max(0, expiresAt - now);
  const total = Math.max(1, expiresAt - createdAt);
  const percent = Math.max(0, Math.min(100, (remaining / total) * 100));

  fill.style.width = `${percent}%`;
  fill.classList.toggle('low', remaining < 30_000);

  if (remaining <= 0) {
    value.textContent = 'Expired — waiting for sweeper';
    value.classList.add('timer-expired');

    if (!state.expiryRefresh) {
      state.expiryRefresh = true;
      setTimeout(() => void refreshReservation({ quiet: true }), 1400);
    }
  } else {
    value.textContent = formatDuration(remaining);
  }
}

// ---------------------------------------------------------------- race

async function runRace() {
  const seat = selectedSeat();
  if (!seat || state.raceRunning) return;

  const count = Math.max(2, Math.min(120, Number.parseInt(els.raceCount.value, 10) || 25));
  els.raceCount.value = String(count);

  state.raceRunning = true;
  state.race = { count, running: true };
  els.raceResults.replaceChildren(el('div', { class: 'race-summary', text: `Running ${count} concurrent holds…` }));
  syncControls();

  const started = performance.now();

  const results = await Promise.all(
    Array.from({ length: count }, () => apiFetch(`${API}/reservations`, {
      method: 'POST',
      body: {
        eventId: state.eventId,
        seatId: seat.id,
        customerId: `race-${Math.random().toString(36).slice(2, 8)}`,
      },
      headers: { 'Idempotency-Key': uuid() },
    })),
  );

  const duration = performance.now() - started;
  const latencies = results.map((result) => result.ms).sort((a, b) => a - b);
  const winners = results.filter((result) => (result.status === 201 || result.status === 200) && result.data?.id);
  const counts = new Map();

  for (const result of results) {
    const key = result.status === 0 ? 'ERR' : String(result.status);
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }

  const average = latencies.reduce((sum, value) => sum + value, 0) / Math.max(1, latencies.length);
  const p95 = latencies[Math.min(latencies.length - 1, Math.ceil(latencies.length * 0.95) - 1)] ?? 0;

  state.race = {
    count,
    running: false,
    duration,
    counts,
    average,
    p95,
    max: latencies.at(-1) ?? 0,
    winner: winners[0]?.data ?? null,
    winnerCount: winners.length,
  };

  if (winners.length > 1) {
    toast(`Invariant violated: ${winners.length} holds succeeded on one seat.`, 'error', 9000);
  } else if (winners.length === 1) {
    state.reservation = winners[0].data;
    state.lastHold = null;
    els.replayBtn.hidden = true;
    toast('One request won the seat; the rest were rejected.', 'success');
  } else {
    toast('No request could hold the seat.', 'error');
  }

  state.raceRunning = false;
  renderRaceResults();
  renderReservation();
  renderSelectedSeat();
  void refreshAvailability({ quiet: true });
}

function renderRaceResults() {
  const race = state.race;
  els.raceResults.replaceChildren();

  if (!race) return;
  if (race.running) {
    els.raceResults.replaceChildren(el('div', { class: 'race-summary', text: `Running ${race.count} concurrent holds…` }));
    return;
  }

  const summary = el('div', { class: 'race-summary' },
    el('strong', { text: `${race.count} requests` }),
    el('span', { text: `completed in ${seconds(race.duration)}` }));

  const badges = [...race.counts.entries()]
    .sort(([a], [b]) => (a === 'ERR' ? 1 : b === 'ERR' ? -1 : Number(a) - Number(b)))
    .map(([status, quantity]) => el('span', {
      class: `badge ${raceBadgeClass(status)}`,
      text: `${status} × ${quantity}`,
    }));

  const metrics = el('div', { class: 'race-metrics' },
    metric('avg', `${Math.round(race.average)}ms`),
    metric('p95', `${Math.round(race.p95)}ms`),
    metric('max', `${race.max}ms`));

  const note = el('p', { class: 'race-note', html:
    'Exactly one request may own the seat. Losers receive <strong>409</strong>; ' +
    'the partial unique index is the final guard if the allocation strategy ever misses.' });

  els.raceResults.append(summary, el('div', { class: 'race-badges' }, badges), metrics);

  if (race.winner) {
    els.raceResults.append(el('div', { class: 'race-winner' },
      'Winning reservation ',
      el('code', { text: shortId(race.winner.id) }),
      ` · ${money(race.winner.amount, race.winner.currency)}`));
  }

  els.raceResults.append(note);
}

function raceBadgeClass(status) {
  if (status === '201' || status === '200') return 'ok';
  if (status === '409') return 'conflict';
  if (status === '429') return 'limit';
  if (status === 'ERR' || Number(status) >= 500) return 'error';
  return '';
}

function metric(label, value) {
  return el('div', { class: 'metric' },
    el('div', { class: 'metric-value', text: value }),
    el('div', { class: 'metric-label', text: label }));
}

// ---------------------------------------------------------------- refresh

async function refreshAvailability({ quiet = false, log = true } = {}) {
  if (!state.eventId) return;

  const eventId = state.eventId;

  try {
    const availability = await request(`${API}/events/${eventId}/availability`, { log });
    if (state.eventId !== eventId) return;

    state.availability = availability;
    state.lastUpdated = new Date();

    renderSeatMap();
    renderStats();
    renderUpdatedAt();
    renderSelectedSeat();
    renderReservation();
  } catch (error) {
    if (!quiet) handleError(error, 'Refreshing availability');
  }
}

// ---------------------------------------------------------------- create event

function defaultStartValue() {
  const date = new Date();
  date.setDate(date.getDate() + 30);
  date.setHours(19, 30, 0, 0);

  const pad = (value) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function openEventDialog() {
  els.eventForm.reset();
  els.eventError.hidden = true;
  els.eventDate.value = defaultStartValue();
  const isLocal = ['localhost', '127.0.0.1'].includes(location.hostname);
  els.apiKey.value = storageGet('seatguard.apiKey') || (isLocal ? 'dev-admin-key' : '');
  els.sectionList.replaceChildren();
  addSectionRow({ name: 'Orchestra', rows: 8, seatsPerRow: 10, price: 49, currency: 'EUR' });
  els.dialog.showModal();
  els.eventName.focus();
}

function addSectionRow(defaults = { name: '', rows: 6, seatsPerRow: 10, price: 39, currency: 'EUR' }) {
  const row = el('div', { class: 'section-row' });

  row.append(
    sectionField('Section', 'name', 'text', defaults.name, 'Balcony'),
    sectionField('Rows', 'rows', 'number', defaults.rows, ''),
    sectionField('Seats/row', 'seatsPerRow', 'number', defaults.seatsPerRow, ''),
    sectionField('Price', 'price', 'number', defaults.price, ''),
    currencyField(defaults.currency));

  const remove = el('button', {
    class: 'icon-btn',
    type: 'button',
    title: 'Remove section',
    attrs: { 'aria-label': 'Remove section' },
    html: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18"/></svg>',
    onclick: () => {
      if (els.sectionList.querySelectorAll('.section-row').length === 1) {
        toast('An event needs at least one section.', 'info');
        return;
      }
      row.remove();
    },
  });

  row.append(remove);
  els.sectionList.append(row);
}

function sectionField(label, field, type, value, placeholder) {
  const id = `section-${field}-${uuid().slice(0, 8)}`;
  const input = el('input', {
    class: 'input',
    id,
    type,
    value: value ?? '',
    placeholder,
    dataset: { field },
    attrs: {
      min: type === 'number' ? '1' : null,
      step: type === 'number' ? '1' : null,
      required: 'required',
    },
  });

  if (field === 'price') {
    input.setAttribute('min', '0.01');
    input.setAttribute('step', '0.01');
  }

  return el('div', { class: 'field' }, el('label', { class: 'field-label', for: id, text: label }), input);
}

function currencyField(selected) {
  const id = `section-currency-${uuid().slice(0, 8)}`;
  const select = el('select', { class: 'select', id, dataset: { field: 'currency' } },
    ['EUR', 'USD', 'GBP'].map((code) => el('option', { value: code, text: code, selected: code === (selected || 'EUR') })));

  return el('div', { class: 'field' }, el('label', { class: 'field-label', for: id, text: 'Currency' }), select);
}

async function submitEventForm(event) {
  event.preventDefault();
  els.eventError.hidden = true;

  const name = els.eventName.value.trim();
  const venue = els.eventVenue.value.trim();
  const apiKey = els.apiKey.value.trim();
  const start = new Date(els.eventDate.value);

  if (!name || !venue) {
    showEventError('Name and venue are required.');
    return;
  }

  if (!apiKey) {
    showEventError('The admin API key is required.');
    els.apiKey.focus();
    return;
  }

  if (Number.isNaN(start.getTime())) {
    showEventError('Choose a valid start date.');
    return;
  }

  const sections = [...els.sectionList.querySelectorAll('.section-row')].map((row) => ({
    name: row.querySelector('[data-field="name"]').value.trim(),
    rows: Number.parseInt(row.querySelector('[data-field="rows"]').value, 10),
    seatsPerRow: Number.parseInt(row.querySelector('[data-field="seatsPerRow"]').value, 10),
    priceAmount: Number.parseFloat(row.querySelector('[data-field="price"]').value),
    currency: row.querySelector('[data-field="currency"]').value,
  }));

  const invalid = sections.find((section) => !section.name
    || !Number.isFinite(section.rows) || section.rows < 1
    || !Number.isFinite(section.seatsPerRow) || section.seatsPerRow < 1
    || !Number.isFinite(section.priceAmount) || section.priceAmount <= 0);

  if (invalid) {
    showEventError('Every section needs a name, positive rows, seats per row and price.');
    return;
  }

  els.submitEvent.disabled = true;
  els.submitEvent.textContent = 'Creating…';

  try {
    const result = await request(`${API}/events`, {
      method: 'POST',
      body: {
        name,
        venue,
        startsAt: start.toISOString(),
        sections,
      },
      headers: { 'X-Api-Key': apiKey },
    });

    storageSet('seatguard.apiKey', apiKey);
    els.dialog.close();

    toast(`Event created with ${result.seatCount.toLocaleString('en-US')} seats.`, 'success');
    await loadEvents(result.eventId);
  } catch (error) {
    if (error instanceof ApiError) {
      showEventError(problemMessage(error));
    } else {
      showEventError(error.message || String(error));
    }
  } finally {
    els.submitEvent.disabled = false;
    els.submitEvent.textContent = 'Create event';
  }
}

function problemMessage(error) {
  const problem = error.problem;
  if (!problem) return error.message;

  const details = problem.errors
    ? Object.values(problem.errors).flat().slice(0, 4)
    : [];

  return [problem.detail || problem.title || error.message, ...details].join(' ');
}

function showEventError(message) {
  els.eventError.textContent = message;
  els.eventError.hidden = false;
}

// ---------------------------------------------------------------- wiring

function bindEvents() {
  els.eventSelect.addEventListener('change', () => void selectEvent(els.eventSelect.value));
  els.refreshBtn.addEventListener('click', () => void refreshAvailability());
  els.newEventBtn.addEventListener('click', openEventDialog);
  els.reserveBtn.addEventListener('click', () => void reserveSeat());
  els.replayBtn.addEventListener('click', () => void replayHold());
  els.raceBtn.addEventListener('click', () => void runRace());
  els.clearLog.addEventListener('click', () => {
    state.log = [];
    renderLog();
  });

  els.regenerateCustomer.addEventListener('click', () => {
    els.customerId.value = generateCustomerId();
    storageSet('seatguard.customerId', els.customerId.value);
  });

  els.customerId.addEventListener('change', () => storageSet('seatguard.customerId', els.customerId.value.trim()));

  els.useIdempotency.addEventListener('change', () => {
    storageSet('seatguard.idempotency', String(els.useIdempotency.checked));
  });

  els.autoRefresh.addEventListener('change', () => {
    state.autoRefresh = els.autoRefresh.checked;
    storageSet('seatguard.autoRefresh', String(state.autoRefresh));
  });

  els.seatMap.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-seat-id]');
    if (button) selectSeat(button.dataset.seatId);
  });

  els.closeDialog.addEventListener('click', () => els.dialog.close());
  els.cancelEvent.addEventListener('click', () => els.dialog.close());
  els.addSection.addEventListener('click', () => addSectionRow());
  els.eventForm.addEventListener('submit', (event) => void submitEventForm(event));
  els.dialog.addEventListener('cancel', () => { els.eventError.hidden = true; });
}

async function bootstrap() {
  bindEvents();

  els.customerId.value = storageGet('seatguard.customerId') || generateCustomerId();
  els.useIdempotency.checked = storageGet('seatguard.idempotency') !== 'false';
  els.autoRefresh.checked = storageGet('seatguard.autoRefresh') !== 'false';
  state.autoRefresh = els.autoRefresh.checked;

  setApiStatus('checking');
  void checkHealth();
  setInterval(() => void checkHealth(), 15_000);

  setInterval(tickReservation, 500);
  setInterval(() => {
    if (state.autoRefresh && document.visibilityState === 'visible') {
      void refreshAvailability({ quiet: true, log: false });
    }
  }, 3000);

  await loadEvents();
}

void bootstrap();
