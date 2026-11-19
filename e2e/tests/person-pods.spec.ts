import { test, expect, type Page } from '@playwright/test';
import { uniqueName } from './helpers';

async function createPerson(page: Page, name: string) {
  await page.goto('/Admin/People/Create');
  await page.locator('#DisplayName').fill(name);
  await page.locator('#ResourceTypeId').selectOption({ index: 1 });
  await page.locator('#BusinessUnitId').selectOption({ index: 1 });
  await page.locator('#ResourcingClassId').selectOption({ label: 'Internal' });
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page).toHaveURL(/\/Admin\/People(\?|$)/);
}

async function assertFit(page: Page, label: string) {
  const fit = await page.locator('main > .row > .col-lg-10').evaluate(el => ({
    clientWidth: el.clientWidth, scrollWidth: el.scrollWidth,
    left: el.getBoundingClientRect().left, right: el.getBoundingClientRect().right, viewport: window.innerWidth
  }));
  console.log(label, JSON.stringify(fit));
  expect(fit.scrollWidth).toBeLessThanOrEqual(fit.clientWidth);
  expect(fit.left).toBeGreaterThanOrEqual(0);
  expect(fit.right).toBeLessThanOrEqual(fit.viewport);
  const picker = await page.locator('[data-filter-picker]').evaluate(el => ({ clientWidth: el.clientWidth, scrollWidth: el.scrollWidth }));
  expect(picker.scrollWidth).toBeLessThanOrEqual(picker.clientWidth);
}

test('pod member name filter is substring based, case insensitive, keyboard accessible and preserves hidden selections', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  const tag = uniqueName('roster');
  const firstName = `Ada MixedCase Engineer ${tag}`;
  const secondName = `Ben Unrelated Analyst ${tag}`;
  await createPerson(page, firstName);
  await createPerson(page, secondName);
  const pod = uniqueName('Filtered pod');
  await page.goto('/Admin/Pods/Create');
  await page.locator('#Name').fill(pod);
  await page.locator('#BusinessUnitId').selectOption({ index: 1 });
  const filter = page.getByRole('searchbox', { name: 'Filter people by name' });
  const first = page.locator('[data-filter-name]', { hasText: firstName });
  const second = page.locator('[data-filter-name]', { hasText: secondName });
  await filter.fill('mIxEdCaSe');
  await expect(first).toBeVisible();
  await expect(second).toBeHidden();
  await expect(page.locator('[data-filter-status]')).toHaveText(/1 of \d+ shown/);
  await filter.press('Tab');
  await expect(first.getByRole('checkbox')).toBeFocused();
  await page.keyboard.press('Space');
  await expect(first.getByRole('checkbox')).toBeChecked();
  await first.getByRole('spinbutton').fill('40');
  await filter.fill('unrelated');
  await expect(first).toBeHidden();
  await second.getByRole('checkbox').check();
  await filter.fill('no-such-roster-person');
  await expect(page.locator('[data-filter-status]')).toHaveText(/0 of \d+ shown/);
  await filter.fill('');
  await expect(first.getByRole('checkbox')).toBeChecked();
  await expect(second.getByRole('checkbox')).toBeChecked();
  await assertFit(page, 'POD_MEMBER_PICKER_FIT 1024');
  await filter.fill('unrelated');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await page.locator('.card', { hasText: pod }).getByRole('link', { name: 'Edit', exact: true }).click();
  await expect(first.getByRole('checkbox')).toBeChecked();
  await expect(first.getByRole('spinbutton')).toHaveValue(/^40(?:\.0+)?$/);
  await expect(second.getByRole('checkbox')).toBeChecked();
});

test('person create and edit offer filterable pod selections with percentage links and preserve hidden choices', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  const pod = uniqueName('Ownership searchable pod');
  await page.goto('/Admin/Pods/Create');
  await page.locator('#Name').fill(pod);
  await page.locator('#BusinessUnitId').selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await page.goto('/Admin/People/Create');
  const person = uniqueName('Pod owner');
  await page.locator('#DisplayName').fill(person);
  await page.locator('#ResourceTypeId').selectOption({ index: 1 });
  await page.locator('#BusinessUnitId').selectOption({ index: 1 });
  await page.locator('#ResourcingClassId').selectOption({ label: 'Internal' });
  const filter = page.getByRole('searchbox', { name: 'Filter pods' });
  await filter.fill('SEARCHABLE');
  const row = page.locator('[data-filter-name]', { hasText: pod });
  await expect(row).toBeVisible();
  await filter.press('Tab');
  await expect(row.getByRole('checkbox')).toBeFocused();
  await page.keyboard.press('Space');
  await expect(row).toContainText('100%');
  await expect(row.getByRole('link')).toHaveAttribute('href', /\/Admin\/Pods\/Edit\/\d+/);
  await assertFit(page, 'PERSON_POD_PICKER_FIT 1024');
  await filter.fill('no-such-pod');
  await expect(row).toBeHidden();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await page.getByRole('textbox', { name: 'Search people', exact: true }).fill(person);
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await page.getByRole('link', { name: 'Edit', exact: true }).click();
  await expect(row.getByRole('checkbox')).toBeChecked();
  await expect(row).toContainText('100%');
  await filter.fill('no-such-pod');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await page.goto('/Admin/People?search=' + encodeURIComponent(person));
  await page.getByRole('link', { name: 'Edit', exact: true }).click();
  await expect(row.getByRole('checkbox')).toBeChecked();
});
