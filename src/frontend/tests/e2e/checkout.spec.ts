import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';

const token = 'b'.repeat(64);
const item = { productId: 1, name: 'OctoCAT supplies', imgName: null, unitPrice: 16.99, quantity: 2, lineTotal: 33.98 };
const savedCart = { cartId: token, items: [item], total: 33.98 };
const receipt = {
  orderId: 42, branchId: 1, orderDate: '2026-10-23T12:00:00Z', status: 'confirmed',
  items: [item], total: 33.98, currency: 'usd',
};

async function checkoutFixture(page: Page, options: { fail?: boolean; paid?: boolean } = {}) {
  let paid = options.paid ?? false;
  let active = false;
  let fail = options.fail ?? false;
  let starts = 0;
  await page.addInitScript((id) => localStorage.setItem('octocat.cartId', id), token);
  await page.route('**/api/branches', (route) => route.fulfill({
    json: [{ branchId: 1, name: 'OctoCAT Central' }],
  }));
  await page.route(`**/api/carts/${token}`, (route) => route.fulfill({
    json: paid ? { ...savedCart, items: [], total: 0 } : savedCart,
  }));
  await page.route(`**/api/carts/${token}/checkout`, async (route) => {
    if (route.request().method() === 'POST') {
      starts++;
      expect(route.request().postDataJSON()).toEqual({ branchId: 1 });
      if (fail) {
        await route.fulfill({ status: 502, json: {
          error: { code: 'PAYMENT_PROVIDER_ERROR', message: 'Payment could not be verified. Your cart is saved.' },
        } });
        return;
      }
      active = true;
    }
    await route.fulfill({ json: paid
      ? { state: 'completed', order: receipt, paymentUrl: null, snapshot: null }
      : { state: active ? 'active' : 'none', order: null,
        paymentUrl: active ? 'https://checkout.stripe.com/test' : null, snapshot: active ? savedCart : null },
    });
  });
  await page.route(`**/api/carts/${token}/checkout/cancel`, (route) => {
    active = false;
    return route.fulfill({ json: { state: 'expired', order: null, paymentUrl: null, snapshot: null } });
  });
  return {
    pay: () => { paid = true; },
    recover: () => { fail = false; },
    starts: () => starts,
  };
}

test('checkout starts from cart, submits only branch, and shows verified order details after payment', async ({ page }) => {
  const fixture = await checkoutFixture(page);
  await page.goto('/cart');
  await page.getByRole('link', { name: 'Proceed to checkout' }).click();
  await expect(page.getByRole('heading', { name: 'Review. Pay. Ready.' })).toBeFocused();
  await expect(page.getByRole('button', { name: 'Start secure payment' })).toBeDisabled();
  await page.getByLabel('Order branch').selectOption('1');
  await page.getByRole('button', { name: 'Start secure payment' }).click();
  const hosted = page.getByRole('link', { name: 'Continue to secure payment' });
  await expect(hosted).toHaveAttribute('href', 'https://checkout.stripe.com/test');
  await expect(page.getByRole('region', { name: 'Purchase summary' })).toContainText('$33.98');
  expect(fixture.starts()).toBe(1);
  fixture.pay();
  await page.goto('/checkout?payment=return');
  await expect(page.getByRole('heading', { name: 'Order #42' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Order details' })).toContainText('2 × $16.99');
  await expect(page.getByRole('region', { name: 'Order details' })).toContainText('$33.98');
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Order #42' })).toBeVisible();
  await page.getByRole('link', { name: 'Back to cart' }).click();
  await expect(page.getByRole('heading', { name: 'Your cart is empty' })).toBeVisible();
});

test('provider failure is explicit and leaves cart and browser identity intact', async ({ page }) => {
  const fixture = await checkoutFixture(page, { fail: true });
  await page.goto('/checkout');
  await page.getByLabel('Order branch').selectOption('1');
  await page.getByRole('button', { name: 'Start secure payment' }).click();
  await expect(page.getByRole('alert')).toContainText('Payment could not be verified. Your cart is saved.');
  expect(await page.evaluate(() => localStorage.getItem('octocat.cartId'))).toBe(token);
  fixture.recover();
  await page.getByRole('button', { name: 'Check payment again' }).click();
  await page.getByRole('link', { name: 'Back to cart' }).click();
  await expect(page.getByLabel('Cart total', { exact: true })).toHaveText('$33.98');
});

test('unpaid redirect never confirms order and cancel returns to unchanged editable cart', async ({ page }) => {
  await checkoutFixture(page);
  await page.goto('/checkout?payment=return');
  await expect(page.getByRole('heading', { name: 'Order #42' })).toHaveCount(0);
  await page.getByLabel('Order branch').selectOption('1');
  await page.getByRole('button', { name: 'Start secure payment' }).click();
  await expect(page.getByText('A declined card does not create an order.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Cancel payment and edit cart' }).click();
  await expect(page.getByRole('status')).toContainText('Payment was canceled or expired.');
  await page.getByRole('link', { name: 'Back to cart' }).click();
  await expect(page.getByLabel('Cart total', { exact: true })).toHaveText('$33.98');
});

for (const width of [390, 768, 1280]) {
  test(`checkout form works by keyboard without horizontal overflow at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await checkoutFixture(page);
    await page.goto('/checkout');
    const branch = page.getByLabel('Order branch');
    await expect(branch).toBeEnabled();
    await branch.focus();
    await page.keyboard.press('ArrowDown');
    await expect(branch).toHaveValue('1');
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Start secure payment' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: 'Continue to secure payment' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  });
}
