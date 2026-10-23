import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';

const apiUrl = process.env.CART_TEST_API_URL ?? 'http://localhost:3000';
const storageKey = 'octocat.cartId';
const productIds: number[] = [];
const discountedName = 'Cart test discounted';
const roundingName = 'Cart test rounding';

test.beforeAll(async ({ request }) => {
  for (const [name, price, discount] of [
    [discountedName, 19.99, 0.15],
    [roundingName, 1.01, 0.5],
  ] as const) {
    const response = await request.post(`${apiUrl}/api/products`, {
      data: {
        supplierId: 1, name, description: 'Isolated cart browser-test fixture',
        price, discount, sku: `CART-E2E-${name}`, unit: 'each', imgName: 'feeder.png',
      },
    });
    expect(response.status()).toBe(201);
    productIds.push((await response.json()).productId);
  }
});

test.afterAll(async ({ request }) => {
  for (const productId of productIds) {
    expect((await request.delete(`${apiUrl}/api/products/${productId}`)).status()).toBe(204);
  }
});

async function addFromCatalog(page: Page, name: string, quantity: number) {
  await page.goto('/products');
  await page.getByLabel('Search products', { exact: true }).fill(name);
  const increase = page.getByRole('button', { name: `Increase quantity of ${name}`, exact: true });
  await expect(increase).toBeEnabled();
  for (let index = 0; index < quantity; index++) {
    await increase.click();
  }
  await page.getByRole('button', { name: `Add ${quantity} ${name} to cart`, exact: true }).click();
  await expect(page.getByText(`Added ${quantity} items to your cart.`, { exact: true })).toBeVisible();
}

test('cart lifecycle persists through refresh and shows exact discounted totals', async ({ page, request, browser }) => {
  await addFromCatalog(page, discountedName, 2);
  await addFromCatalog(page, discountedName, 1);
  await addFromCatalog(page, roundingName, 3);
  await page.getByRole('link', { name: 'Cart', exact: true }).click();
  const total = page.getByLabel('Cart total', { exact: true });
  await expect(total).toHaveText('$52.50');
  await page.reload();
  await expect(total).toHaveText('$52.50');
  await expect(page.getByRole('spinbutton', { name: `Quantity for ${discountedName}`, exact: true })).toHaveValue('3');

  const token = await page.evaluate((key) => localStorage.getItem(key), storageKey);
  const snapshot = await request.get(`${apiUrl}/api/carts/${token}`);
  expect(snapshot.status()).toBe(200);
  expect((await snapshot.json()).total).toBe(52.50);

  const quantity = page.getByRole('spinbutton', { name: `Quantity for ${discountedName}`, exact: true });
  await quantity.fill('1');
  await quantity.press('Tab');
  await expect(page.getByRole('button', { name: `Update quantity of ${discountedName}` })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(total).toHaveText('$18.52');
  await page.reload();
  await expect(quantity).toHaveValue('1');
  await page.getByRole('button', { name: `Remove ${discountedName}`, exact: true }).click();
  await expect(total).toHaveText('$1.53');
  await expect(page.getByRole('heading', { name: 'Your cart', exact: true })).toBeFocused();
  await page.reload();
  await expect(page.getByRole('heading', { name: discountedName, exact: true })).toHaveCount(0);

  const separate = await browser.newContext();
  try {
    const otherPage = await separate.newPage();
    await otherPage.goto(new URL('/cart', page.url()).toString());
    await expect(otherPage.getByRole('heading', { name: 'Your cart is empty' })).toBeVisible();
    await expect(otherPage.getByLabel('Cart total', { exact: true })).toHaveText('$0.00');
  } finally {
    await separate.close();
  }

  await page.getByRole('button', { name: `Remove ${roundingName}`, exact: true }).click();
  await expect(total).toHaveText('$0.00');
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Your cart is empty' })).toBeVisible();
  await expect(total).toHaveText('$0.00');
  expect((await (await request.get(`${apiUrl}/api/carts/${token}`)).json()).items).toEqual([]);
});

test('failed add keeps quantity and pending controls prevent duplicate submission', async ({ page }) => {
  await page.route('**/api/carts/*/items', async (route) => {
    if (route.request().method() === 'POST') {
      await new Promise((resolve) => setTimeout(resolve, 400));
      await route.fulfill({
        status: 503, contentType: 'application/json',
        body: JSON.stringify({ error: { code: 'UNAVAILABLE', message: 'Cart temporarily unavailable' } }),
      });
    } else {
      await route.continue();
    }
  });
  await page.goto('/products');
  await page.getByLabel('Search products').fill(discountedName);
  const increase = page.getByRole('button', { name: `Increase quantity of ${discountedName}`, exact: true });
  await expect(increase).toBeEnabled();
  await increase.click();
  const add = page.getByRole('button', { name: `Add 1 ${discountedName} to cart`, exact: true });
  await add.click();
  await expect(add).toBeDisabled();
  await expect(increase).toBeDisabled();
  await expect(page.getByRole('alert')).toContainText('Cart temporarily unavailable');
  await expect(page.locator(`#qty-${productIds[0]}`)).toHaveText('1');
  await expect(page.getByText('Added 1 items to your cart.', { exact: true })).toHaveCount(0);
  const token = await page.evaluate((key) => localStorage.getItem(key), storageKey);
  expect(token).toBeTruthy();
  await page.getByRole('link', { name: 'View cart and retry' }).click();
  await expect(page.getByLabel('Cart total', { exact: true })).toHaveText('$0.00');
});

test('unavailable saved cart requires an explicit reset', async ({ page }) => {
  await page.goto('/');
  await page.evaluate((key) => localStorage.setItem(key, 'a'.repeat(64)), storageKey);
  await page.goto('/cart');
  await expect(page.getByRole('alert')).toContainText('no longer available');
  expect(await page.evaluate((key) => localStorage.getItem(key), storageKey)).toBe('a'.repeat(64));
  await page.getByRole('button', { name: 'Start new cart' }).click();
  await expect(page.getByRole('heading', { name: 'Your cart is empty' })).toBeVisible();
  expect(await page.evaluate((key) => localStorage.getItem(key), storageKey)).toBeNull();
});

test('cart storage read failures are visible instead of pretending to save a cart', async ({ page }) => {
  await page.addInitScript(() => {
    const original = Storage.prototype.getItem;
    Storage.prototype.getItem = function (key) {
      if (key === 'octocat.cartId') {
        throw new DOMException('Storage access denied', 'SecurityError');
      }
      return original.call(this, key);
    };
  });
  await page.goto('/cart');
  await expect(page.getByRole('alert')).toContainText('Storage access denied');
  await expect(page.getByLabel('Cart total', { exact: true })).toHaveCount(0);
});

test('cart storage write failures preserve quantity and do not claim success', async ({ page }) => {
  await page.addInitScript(() => {
    const original = Storage.prototype.setItem;
    Storage.prototype.setItem = function (key, value) {
      if (key === 'octocat.cartId') {
        throw new DOMException('Cannot save cart identifier', 'QuotaExceededError');
      }
      original.call(this, key, value);
    };
  });
  await page.goto('/products');
  await page.getByLabel('Search products').fill(discountedName);
  const increase = page.getByRole('button', { name: `Increase quantity of ${discountedName}`, exact: true });
  await expect(increase).toBeEnabled();
  await increase.click();
  await page.getByRole('button', { name: `Add 1 ${discountedName} to cart`, exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Cannot save cart identifier');
  await expect(page.locator(`#qty-${productIds[0]}`)).toHaveText('1');
  expect(await page.evaluate((key) => localStorage.getItem(key), storageKey)).toBeNull();
});

test('cart navigation and layout work at mobile, tablet and desktop widths', async ({ page }, testInfo) => {
  await addFromCatalog(page, discountedName, 1);
  for (const width of [320, 375, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    const link = page.getByRole('link', { name: 'Cart', exact: true });
    await expect(link).toBeVisible();
    await link.click();
    await expect(page.getByRole('heading', { name: 'Your cart', exact: true })).toBeVisible();
    await expect(page.getByRole('spinbutton', { name: `Quantity for ${discountedName}` })).toBeVisible();
    const overflow = await page.locator('main').evaluate((element) => element.scrollWidth > element.clientWidth);
    expect(overflow).toBe(false);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    const loginBounds = await page.getByRole('link', { name: 'Login', exact: true }).boundingBox();
    expect(loginBounds).not.toBeNull();
    expect(loginBounds!.x + loginBounds!.width).toBeLessThanOrEqual(width);
    await page.screenshot({ path: testInfo.outputPath(`cart-${width}.png`), fullPage: true, animations: 'disabled' });
  }
  await page.goto('/products');
  await page.getByRole('link', { name: 'My Cart', exact: true }).click();
  await expect(page).toHaveURL(/\/cart$/);
  await expect(page.getByRole('heading', { name: 'Your cart', exact: true })).toBeFocused();
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(0);
  await page.getByRole('button', { name: 'Toggle dark/light mode' }).click();
  await expect(page.locator('html')).toHaveClass(/dark/);
  await page.screenshot({ path: testInfo.outputPath('cart-dark.png'), fullPage: true, animations: 'disabled' });
  await page.getByRole('link', { name: 'Login', exact: true }).click();
  await page.getByLabel('Email Address').fill('cart-test@example.com');
  await page.getByLabel('Password', { exact: true }).fill('demo-only');
  await page.getByRole('button', { name: 'Login', exact: true }).click();
  await page.setViewportSize({ width: 320, height: 900 });
  await page.getByRole('link', { name: 'Cart', exact: true }).click();
  const logoutBounds = await page.getByRole('button', { name: 'Logout', exact: true }).boundingBox();
  expect(logoutBounds).not.toBeNull();
  expect(logoutBounds!.x + logoutBounds!.width).toBeLessThanOrEqual(320);
  await expect(page.getByLabel('Cart total', { exact: true })).toHaveText('$16.99');
});
