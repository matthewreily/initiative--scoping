import { expect, type Page } from '@playwright/test';

export function uniqueName(prefix: string): string {
  return `${prefix} ${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
}

export function isoDate(daysFromToday: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysFromToday);
  return d.toISOString().slice(0, 10);
}

/** Creates a Draft, T-shirt-sized initiative through the New initiative form and lands on its Details page. */
export async function createInitiative(page: Page, name: string, sizeKey = 'M'): Promise<number> {
  await page.goto('/Initiatives/Create');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.locator('#BusinessUnitId').selectOption({ index: 1 }); // index 0 is "Select…"
  await page.locator('#sizing-method').selectOption('TShirt');
  await page.locator('#size-key').selectOption(sizeKey);
  await page.locator('#target-start').fill(isoDate(0));
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(page).toHaveURL(/\/Initiatives\/Details\/\d+/);
  await expect(page.getByRole('heading', { level: 1 })).toContainText(name);
  return Number(page.url().match(/\/Initiatives\/Details\/(\d+)/)![1]);
}

/** Adds a phase via the inline "Add phase" form on the Plan tab. */
export async function addPhase(page: Page, name: string, start: string, end: string): Promise<void> {
  const form = page.locator('form[action$="/AddPhase"], form[action*="/AddPhase/"]').first();
  await form.locator('input[name="Name"]').fill(name);
  await form.locator('input[name="PlannedStart"]').fill(start);
  await form.locator('input[name="PlannedEnd"]').fill(end);
  await form.getByRole('button', { name: 'Add' }).click();
  await expect(page.locator('#pane-plan table').filter({ hasText: name }).first()).toBeVisible();
}

/** Applies the initiative's T-shirt size, which creates template phases + priced allocations. */
export async function applySize(page: Page, sizeKey = 'M'): Promise<void> {
  const form = page.locator('form[action*="/ApplySize"]').first();
  await form.locator('select[name="Method"]').selectOption('TShirt');
  await form.locator('select[name="SizeKey"]').selectOption(sizeKey);
  await form.locator('input[name="Location"]').fill('Onshore');
  await form.getByRole('button', { name: /apply/i }).click();
  await expect(page).toHaveURL(/\/Initiatives\/Details\/\d+/);
}
