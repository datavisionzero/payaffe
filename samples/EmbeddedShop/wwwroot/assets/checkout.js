// The whole checkout, against this shop's own endpoints. There is no payment-provider SDK in the
// browser, no iframe, no redirect, and no credential: the page knows about orders, and the shop
// backend is the only thing that knows there is a payment provider at all.

const base = `/${window.location.pathname.split('/').filter(Boolean)[0] ?? ''}`;

const elements = {
  shopName: document.getElementById('shop-name'),
  items: document.getElementById('items'),
  checkout: document.getElementById('checkout'),
  orderLine: document.getElementById('order-line'),
  currencyChoice: document.getElementById('currency-choice'),
  currencies: document.getElementById('currencies'),
  instruction: document.getElementById('instruction'),
  qr: document.getElementById('qr'),
  amount: document.getElementById('amount'),
  address: document.getElementById('address'),
  expires: document.getElementById('expires'),
  walletLink: document.getElementById('wallet-link'),
  status: document.getElementById('status'),
  error: document.getElementById('error'),
};

let order = null;
let pollTimer = null;
let currency = null;

const money = (minor, code) =>
  new Intl.NumberFormat(undefined, { style: 'currency', currency: code }).format(minor / 100);

async function call(path, options) {
  const response = await fetch(`${base}${path}`, {
    headers: { 'content-type': 'application/json' },
    ...options,
  });
  const body = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) {
    const error = new Error(body?.error ?? `request_failed_${response.status}`);
    error.body = body;
    throw error;
  }
  return body;
}

function showError(message) {
  elements.error.hidden = false;
  elements.error.textContent = message;
}

function clearError() {
  elements.error.hidden = true;
  elements.error.textContent = '';
}

async function loadCatalogue() {
  const catalogue = await call('/api/catalog');
  elements.shopName.textContent = catalogue.name;
  elements.items.replaceChildren(
    ...catalogue.items.map((item) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = `Buy — ${money(item.priceMinor, catalogue.currency)}`;
      button.addEventListener('click', () => placeOrder(item.sku));

      const entry = document.createElement('li');
      entry.append(Object.assign(document.createElement('span'), { textContent: item.name }), button);
      return entry;
    }),
  );
}

async function placeOrder(sku) {
  clearError();
  try {
    order = await call('/api/orders', { method: 'POST', body: JSON.stringify({ sku }) });
    elements.checkout.hidden = false;
    elements.orderLine.textContent = `${order.item} — ${money(order.amountMinor, order.fiatCurrency)}`;
    renderCurrencies();
    startPolling();
  } catch (failure) {
    showError(
      failure.message === 'payments_unavailable'
        ? 'Payments are unavailable right now. Nothing was charged; please try again.'
        : 'The order could not be placed.',
    );
  }
}

function renderCurrencies() {
  elements.currencies.replaceChildren(
    ...order.options.map((option) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = option.currency;
      button.disabled = option.status !== 'available';
      button.title =
        option.status === 'available'
          ? ''
          : `Unavailable: ${option.unavailableReason ?? 'no reason given'}`;
      button.addEventListener('click', () => selectCurrency(option.currency));
      return button;
    }),
  );
}

async function selectCurrency(chosen) {
  clearError();
  currency = chosen;
  try {
    order = await call('/api/orders/' + order.orderId + '/currency', {
      method: 'POST',
      body: JSON.stringify({ currency: chosen }),
    });
    renderInstruction();
  } catch (failure) {
    if (failure.message === 'rate_unavailable') {
      showError('No exchange rate for that currency at the moment. Pick one again in a minute.');
      return;
    }
    if (failure.message === 'payment_expired') {
      showError('This checkout expired before a currency was chosen. Start a new order.');
      elements.currencyChoice.hidden = true;
      return;
    }
    showError('That currency could not be selected.');
  }
}

function renderInstruction() {
  if (!order.instruction) {
    return;
  }

  elements.currencyChoice.hidden = true;
  elements.instruction.hidden = false;
  elements.amount.textContent = `${order.instruction.amount} ${order.instruction.currency}`;
  elements.address.textContent = order.instruction.address;
  // Served by this shop, from this origin. The image is not fetched from the payment provider.
  elements.qr.src = order.instruction.qrCodeUrl;
  elements.qr.alt = `Payment code for ${order.instruction.amount} ${order.instruction.currency}`;
  // A wallet URI is a scheme the payer's own wallet handles. Following it is the payer's action
  // and goes to their wallet, not to a web origin.
  elements.walletLink.href = order.instruction.walletUri;
  elements.expires.textContent = new Date(order.instruction.expiresAt).toLocaleString();
}

function startPolling() {
  clearInterval(pollTimer);
  renderStatus();
  pollTimer = setInterval(async () => {
    try {
      order = await call(`/api/orders/${order.orderId}`);
      if (order.instruction) {
        renderInstruction();
      }
      renderStatus();
    } catch {
      // A failed status read is not a failed payment. The next tick tries again.
    }
  }, 3000);
}

function renderStatus() {
  // The page never decides that an order is paid. It shows what the shop backend concluded from
  // a verified webhook or a reconciling read, and nothing else.
  if (order.fulfillment === 'Fulfilled') {
    clearInterval(pollTimer);
    elements.instruction.hidden = true;
    elements.status.textContent = 'Paid. Your order is on its way.';
    return;
  }

  if (order.fulfillment === 'Expired') {
    elements.status.textContent =
      'This checkout expired. If you already sent the transfer, it can still arrive; ' +
      'the order stays open until it is reconciled.';
    return;
  }

  const waiting = {
    none: 'Preparing the payment…',
    pending_currency_selection: 'Choose how you want to pay.',
    waiting_for_payment: currency
      ? 'Waiting for your transfer. This page updates on its own.'
      : 'Waiting for your transfer.',
    observed: 'Transfer seen. Waiting for confirmations.',
  };
  elements.status.textContent = waiting[order.paymentState] ?? `Payment state: ${order.paymentState}`;
}

function copyOnClick(id, read) {
  document.getElementById(id).addEventListener('click', async () => {
    await navigator.clipboard.writeText(read());
  });
}

copyOnClick('copy-address', () => order.instruction.address);
copyOnClick('copy-amount', () => order.instruction.amount);

loadCatalogue().catch(() => showError('The shop could not be loaded.'));
