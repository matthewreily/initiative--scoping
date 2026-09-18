import { test, expect } from '@playwright/test';
import { createInitiative, uniqueName } from './helpers';

test.describe('In-app help', () => {
  test('first visit starts the welcome tour once; it can be stepped through and replayed', async ({ browser }) => {
    // Fresh browser storage: the shared config marks tours as seen so they never cover other tests.
    const context = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    const page = await context.newPage();

    await page.goto('/');
    const tour = page.getByRole('dialog', { name: 'Welcome' });
    await expect(tour).toBeVisible();
    await expect(tour.getByText('1 of')).toBeVisible();
    await expect(tour.getByRole('button', { name: 'Back' })).toBeDisabled();

    await tour.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByRole('dialog', { name: 'Initiatives' })).toBeVisible();
    await page.keyboard.press('ArrowLeft');
    await expect(page.getByRole('dialog', { name: 'Welcome' })).toBeVisible();

    await page.getByRole('button', { name: 'Skip tour' }).click();
    await expect(page.locator('.tour-overlay')).toHaveCount(0);

    await page.reload();
    await expect(page.locator('.tour-overlay')).toHaveCount(0);

    await page.getByRole('button', { name: 'Help' }).click();
    await page.getByRole('button', { name: 'Take the tour' }).click();
    await expect(page.getByRole('dialog', { name: 'Welcome' })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.locator('.tour-overlay')).toHaveCount(0);
    await context.close();
  });

  test('metric hints show glossary tooltips without sorting the column, and the Help page lists them', async ({ page }) => {
    await createInitiative(page, uniqueName('Help E2E'));
    await page.goto('/Initiatives');
    const header = page.locator('th', { hasText: 'Forecast Cost' }).first();
    const hint = header.getByRole('button', { name: 'What is Forecast cost?' });
    await hint.hover();
    await expect(page.getByRole('tooltip')).toContainText('What the current plan is expected to cost');
    await hint.click();
    await expect(header).not.toHaveAttribute('aria-sort', /ascending|descending/);

    await page.getByRole('button', { name: 'Help' }).click();
    await page.getByRole('link', { name: 'Glossary' }).click();
    await expect(page).toHaveURL(/\/Home\/Help$/);
    await expect(page.locator('#term-eac')).toContainText('Estimate at completion');
    await page.getByRole('button', { name: 'Keyboard shortcuts' }).click();
    await expect(page.locator('#shortcut-help')).toBeVisible();
  });
});
