import { test, expect } from '@playwright/test';
import { isoDate, uniqueName } from './helpers';

test.describe('Admin rate cards', () => {
  test('create a rate card, import entries from CSV, export them back', async ({ page }) => {
    const name = uniqueName('E2E Card');

    await page.goto('/Admin/RateCards/Create');
    await page.getByLabel('Name', { exact: true }).fill(name);
    await page.locator('#EffectiveStart').fill(isoDate(365));
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page).toHaveURL(/\/Admin\/RateCards\/Details\/\d+/);
    await expect(page.getByRole('heading', { level: 2 }).first()).toContainText(name);

    const csv = [
      'ResourceType,Seniority,Location,ResourcingClass,HourlyRate,Vendor,Discipline',
      'Software Engineer,Senior,Onshore,Internal,123.45,,',
      'E2E Data Scientist,Lead,Offshore,Vendor,99,,Engineering'
    ].join('\n');
    await page.locator('input[type="file"][name="File"]').setInputFiles({
      name: 'rates.csv',
      mimeType: 'text/csv',
      buffer: Buffer.from(csv, 'utf8')
    });
    await page.getByRole('button', { name: 'Import', exact: true }).click();

    await expect(page).toHaveURL(/\/Admin\/RateCards\/Details\/\d+/);
    await expect(page.getByText('Import rejected')).toHaveCount(0);
    const entries = page.locator('table').filter({ hasText: 'Software Engineer' }).first();
    await expect(entries).toContainText('E2E Data Scientist');
    const engineerRow = entries.locator('tbody tr').filter({ hasText: 'Software Engineer' });
    await expect(engineerRow.locator('input[type="number"]')).toHaveValue(/^123\.45/);

    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('link', { name: 'Export CSV' }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/\.csv$/i);
    const stream = await download.createReadStream();
    const chunks: Buffer[] = [];
    for await (const chunk of stream) chunks.push(Buffer.from(chunk));
    const exported = Buffer.concat(chunks).toString('utf8');
    expect(exported).toContain('ResourceType,Seniority,Location,ResourcingClass,HourlyRate');
    expect(exported).toContain('E2E Data Scientist');

    // the card is in the list as a draft
    await page.goto('/Admin/RateCards');
    await expect(page.getByRole('link', { name })).toBeVisible();
  });
});
