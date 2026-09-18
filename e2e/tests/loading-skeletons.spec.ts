import { test, expect, type Page } from '@playwright/test';

/**
 * The placeholder only exists between click and response, when the old document is already being torn
 * down and Playwright locators cannot evaluate. Record what appeared with a MutationObserver in the old
 * page (persisted to sessionStorage, which survives the same-origin navigation) and read it afterwards.
 */
async function observeLoading(page: Page): Promise<void> {
  await page.evaluate(() => {
    sessionStorage.removeItem('e2e:loading');
    const record = () => {
      const skeleton = document.querySelector('#main [data-skeleton]');
      const state = {
        skeleton: !!skeleton,
        busy: skeleton?.getAttribute('aria-busy') ?? null,
        title: skeleton?.querySelector('h1')?.textContent ?? null,
        progress: document.querySelector('.page-progress')?.classList.contains('is-active') ?? false
      };
      const prev = JSON.parse(sessionStorage.getItem('e2e:loading') ?? '{}');
      sessionStorage.setItem('e2e:loading', JSON.stringify({ ...prev, ...Object.fromEntries(Object.entries(state).filter(([, v]) => v)) }));
    };
    new MutationObserver(record).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
  });
}

async function recorded(page: Page): Promise<{ skeleton?: boolean; busy?: string; title?: string; progress?: boolean }> {
  return page.evaluate(() => JSON.parse(sessionStorage.getItem('e2e:loading') ?? '{}'));
}

test.describe('loading skeletons', () => {
  for (const name of ['Portfolio', 'Capacity']) {
    test(`navigating to ${name} shows a placeholder layout until the page arrives`, async ({ page }) => {
      await page.goto('/');
      await observeLoading(page);
      await page.getByRole('navigation').getByRole('link', { name, exact: true }).click();

      await expect(page).toHaveURL(new RegExp(`/${name}`, 'i'));
      expect(await recorded(page)).toEqual({ skeleton: true, busy: 'true', title: name, progress: true });
      await expect(page.locator('#main [data-skeleton]')).toHaveCount(0);
      await expect(page.locator('.page-progress')).not.toHaveClass(/is-active/);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(name);
    });
  }

  test('filter form on Portfolio shows the skeleton while reloading', async ({ page }) => {
    await page.goto('/Portfolio');
    await observeLoading(page);
    await page.locator('form[data-remember-filters] button[type=submit]').click();
    await page.waitForLoadState();
    expect(await recorded(page)).toMatchObject({ skeleton: true, title: 'Portfolio' });
    await expect(page.locator('#main [data-skeleton]')).toHaveCount(0);
    await expect(page.locator('form[data-remember-filters]')).toBeVisible();
  });

  test('other links only show the progress bar, not a skeleton', async ({ page }) => {
    await page.goto('/');
    await observeLoading(page);
    await page.getByRole('navigation').getByRole('link', { name: 'Initiatives', exact: true }).click();
    await expect(page).toHaveURL(/\/Initiatives/i);
    expect(await recorded(page)).toEqual({ progress: true });
  });
});
