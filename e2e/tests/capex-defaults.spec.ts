import { test, expect } from '@playwright/test';
import { addPhase, createInitiative, isoDate, uniqueName } from './helpers';

test.describe('Labor Capex % defaults', () => {
  test('Admin → Work calendar shows the defaults and the Add-allocation row follows the resourcing class', async ({ page }) => {
    await page.goto('/Admin/WorkCalendar');
    await expect(page.locator('#InternalCapexPercent')).toHaveValue('70');
    await expect(page.locator('#VendorCapexPercent')).toHaveValue('100');

    await createInitiative(page, uniqueName('E2E Capex'));
    await addPhase(page, 'Build', isoDate(0), isoDate(30));

    const capex = page.locator('#capitalization');
    await expect(capex).toHaveValue('70');

    await page.locator('#resourcingclass').selectOption('Vendor');
    await expect(capex).toHaveValue('100');

    // A hand-typed value is kept when the class changes again.
    await capex.fill('42');
    await page.locator('#resourcingclass').selectOption('InternalFte');
    await expect(capex).toHaveValue('42');
  });
});
