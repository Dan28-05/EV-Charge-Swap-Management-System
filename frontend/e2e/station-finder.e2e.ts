import { test, expect } from '@playwright/test';

const stations = Array.from({ length: 21 }, (_, i) => ({
  id: `station-${i}`,
  name: `Trạm thử ${i}`,
  address: 'Quy Nhơn',
  latitude: 13.766667 + i * 0.005,
  longitude: 109.233333,
  offersCarCharging: i % 3 !== 1,
  offersBatterySwap: i % 3 !== 0,
}));

test('pages, shared selection, directions and responsive layout', async ({
  page,
  isMobile,
}, info) => {
  await page.route('**/api/locations**', async (route) => {
    const url = new URL(route.request().url());
    const id = url.pathname.split('/').pop();
    if (id !== 'locations') return route.fulfill({ json: stations.find((s) => s.id === id) });
    const number = Number(url.searchParams.get('page'));
    return route.fulfill({
      json: {
        items: stations.slice((number - 1) * 20, number * 20),
        page: number,
        pageSize: 20,
        totalCount: 21,
      },
    });
  });
  await page.goto('/');
  await expect(page.locator('.station-card')).toHaveCount(20);
  await expect(page.locator('.station-marker')).toHaveCount(20);
  await page.getByRole('button', { name: 'Tải thêm địa điểm' }).click();
  await expect(page.locator('.station-card')).toHaveCount(21);
  await expect(page.locator('.station-marker')).toHaveCount(21);
  await page.locator('.choose').nth(20).focus();
  await page.locator('.choose').nth(20).press('Enter');
  await expect(page.locator('.detail h2')).toHaveText('Trạm thử 20');
  const url = new URL((await page.locator('.directions').getAttribute('href'))!);
  expect(url.searchParams.get('destination')).toBe(
    `${stations[20].latitude},${stations[20].longitude}`,
  );
  await page.getByRole('button', { name: 'Đóng chi tiết' }).click();
  if (isMobile) await page.getByRole('button', { name: 'Bản đồ', exact: true }).click();
  await page.locator('.station-marker').nth(19).click();
  await expect(page.locator('.detail h2')).toHaveText('Trạm thử 19');
  await expect(page.locator('.station-card.selected strong')).toHaveText('Trạm thử 19');
  await page.getByRole('button', { name: 'Đóng chi tiết' }).click();
  await page.locator('.station-marker').nth(18).focus();
  await page.locator('.station-marker').nth(18).press('Enter');
  await expect(page.locator('.detail h2')).toHaveText('Trạm thử 18');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
  await page.screenshot({ path: info.outputPath('station-finder.png'), fullPage: true });
  if (isMobile) {
    await page.getByRole('button', { name: 'Đóng chi tiết' }).click();
    await page.getByRole('button', { name: 'Danh sách', exact: true }).click();
    await expect(page.locator('.sidebar')).toBeVisible();
  }
});

test('API error, retry and empty state', async ({ page }) => {
  let failed = false;
  await page.route('**/api/locations?**', (route) => {
    if (!failed) {
      failed = true;
      return route.fulfill({ status: 503, json: {} });
    }
    return route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
  });
  await page.goto('/');
  await expect(page.getByRole('alert')).toContainText('Không thể tải');
  await page.getByRole('button', { name: 'Thử lại', exact: true }).click();
  await expect(page.getByText('Chưa có địa điểm', { exact: true })).toBeVisible();
  await expect(page.locator('.station-marker')).toHaveCount(0);
});

test('live API returns PostgreSQL stations beyond page one', async ({ page }, info) => {
  test.skip(
    process.env['LIVE_API'] !== '1',
    'Enable LIVE_API when the local API and seeded PostgreSQL are running.',
  );
  await page.goto('/');
  await expect(page.locator('.station-card')).toHaveCount(20);
  await page.getByRole('button', { name: 'Tải thêm địa điểm' }).click();
  await expect(page.locator('.station-card')).toHaveCount(40);
  await expect(page.locator('.station-marker')).toHaveCount(40);
  await page.locator('.choose').nth(25).click();
  await expect(page.locator('.detail h2')).toHaveText(
    (await page.locator('.station-card strong').nth(25).textContent())!,
  );
  await page.screenshot({ path: info.outputPath('live-api.png'), fullPage: true });
});
