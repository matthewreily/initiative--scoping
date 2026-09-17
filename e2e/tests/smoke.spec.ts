import { test, expect } from '@playwright/test';

test.describe('Smoke', () => {
  test('health endpoint and home page load for the dev user', async ({ page, request }) => {
    const health = await request.get('/health');
    expect(health.ok()).toBeTruthy();

    await page.goto('/');
    await expect(page.getByRole('link', { name: 'Initiatives', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Portfolio', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Capacity', exact: true })).toBeVisible();
    await expect(page.locator('#global-search')).toBeVisible();
  });

  test('main pages render without server errors', async ({ page }) => {
    for (const path of ['/Initiatives', '/Portfolio', '/Capacity', '/Approvals', '/Actuals', '/Audit', '/Admin', '/Admin/RateCards', '/Admin/Users']) {
      const response = await page.goto(path);
      expect(response?.status(), path).toBe(200);
      await expect(page.getByRole('heading', { level: 1 }), path).toBeVisible();
    }
  });

  test('keyboard shortcuts navigate between pages', async ({ page }) => {
    await page.goto('/');
    await page.locator('body').click();
    await page.keyboard.press('g');
    await page.keyboard.press('i');
    await expect(page).toHaveURL(/\/Initiatives$/);
    await page.keyboard.press('g');
    await page.keyboard.press('p');
    await expect(page).toHaveURL(/\/Portfolio$/);
  });
});
