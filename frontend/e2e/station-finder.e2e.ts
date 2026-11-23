import { test, expect } from '@playwright/test';

const stations = Array.from({ length: 21 }, (_, i) => ({
  id: `station-${i}`,
  name: `Trạm thử ${i}`,
  address: 'Quy Nhơn',
  latitude: 13.766667 + i * 0.005,
  longitude: 109.233333,
  operatingStatus: 'Open',
  offersCarCharging: i % 3 !== 1,
  offersBatterySwap: i % 3 !== 0,
  availableChargingBays: 3,
  availableBatteries: 5,
}));

test.use({
  permissions: ['geolocation'],
  geolocation: { latitude: 13.766667, longitude: 109.233333 },
});

test('pages, shared selection, directions and responsive layout', async ({
  page,
  isMobile,
}, info) => {
  await page.route('**/api/locations**', async (route) => {
    const url = new URL(route.request().url());
    const lastSegment = url.pathname.split('/').pop();
    if (lastSegment === 'nearby') {
      const pageNumber = Number(url.searchParams.get('page') ?? '1');
      const slice = stations.slice((pageNumber - 1) * 20, pageNumber * 20);
      return route.fulfill({
        json: {
          items: slice.map((location, index) => ({
            location,
            distanceKm: Number(((pageNumber - 1) * 20 + index) * 0.4),
          })),
          page: pageNumber,
          pageSize: 20,
          totalCount: 21,
        },
      });
    }
    return route.fulfill({ json: stations.find((s) => s.id === lastSegment) });
  });
  await page.goto('/');
  await expect(page.locator('.station-card')).toHaveCount(20);
  await expect(page.locator('.station-marker')).toHaveCount(20);
  await page.getByRole('button', { name: 'Tải thêm trạm' }).click();
  await expect(page.locator('.station-card')).toHaveCount(21);
  await expect(page.locator('.station-marker')).toHaveCount(21);
  await page.locator('.choose').nth(20).focus();
  await page.locator('.choose').nth(20).press('Enter');
  await expect(page.locator('.detail h2')).toHaveText('Trạm thử 20');
  const url = new URL((await page.locator('.detail .directions').getAttribute('href'))!);
  expect(url.searchParams.get('destination')).toBe(
    `${stations[20].latitude},${stations[20].longitude}`,
  );
  await page.getByRole('button', { name: 'Đóng chi tiết' }).click();
  if (isMobile) await page.getByRole('button', { name: 'Bản đồ', exact: true }).click();
  if (isMobile) {
    await page.locator('.station-marker').nth(19).focus();
    await page.locator('.station-marker').nth(19).press('Enter');
  } else {
    await page.locator('.station-marker').nth(19).click();
  }
  await expect(page.locator('.detail h2')).toHaveText('Trạm thử 19');
  await expect(page.locator('.station-card.selected .card-name')).toHaveText('Trạm thử 19');
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
    await page.getByRole('button', { name: /Danh sách/ }).first().click();
    await expect(page.locator('.floating-sidebar')).toBeVisible();
  }
});

test('API error, retry and empty state', async ({ page }) => {
  let failed = false;
  await page.route('**/api/locations/nearby**', (route) => {
    if (!failed) {
      failed = true;
      return route.fulfill({ status: 503, json: {} });
    }
    return route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } });
  });
  await page.goto('/');
  await expect(page.getByRole('alert')).toContainText('Không thể tìm kiếm trạm');
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
  await page.getByRole('button', { name: 'Tải thêm trạm' }).click();
  await expect(page.locator('.station-card')).toHaveCount(40);
  await expect(page.locator('.station-marker')).toHaveCount(40);
  await page.locator('.choose').nth(25).click();
  await expect(page.locator('.detail h2')).toHaveText(
    (await page.locator('.station-card .card-name').nth(25).textContent())!,
  );
  await page.screenshot({ path: info.outputPath('live-api.png'), fullPage: true });
});
