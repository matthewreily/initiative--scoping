import { test, expect } from '@playwright/test';
import { addPhase, createInitiative, isoDate, uniqueName } from './helpers';

test('add and edit offer vendor classes only with an active, priced vendor', async ({ page }) => {
  const vendorName = uniqueName('Priced vendor');
  const typeName = uniqueName('Vendor options engineer');
  await page.goto('/Admin/Vendors/Create');
  await page.locator('#Name').fill(vendorName);
  await page.locator('#IsActive').check();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  const vendorEdit = await page.locator('tbody tr', { hasText: vendorName }).getByRole('link', { name: 'Edit' }).getAttribute('href');

  await page.goto('/Admin/RateCards/Create');
  await page.locator('#Name').fill(uniqueName('Vendor option rates'));
  await page.locator('#EffectiveStart').fill(isoDate(3650));
  await page.locator('#EffectiveEnd').fill(isoDate(3655));
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  const cardUrl = page.url();
  const csv = [
    'ResourceType,Seniority,Location,ResourcingClass,HourlyRate,Vendor,Discipline',
    `${typeName},Senior,Onshore,Internal,100,,Engineering`,
    `${typeName},Senior,Onshore,Vendor,125,${vendorName},Engineering`,
    `${typeName},Senior,Offshore,Internal,80,,Engineering`,
    `${typeName},Mid,Onshore,Internal,75,,Engineering`,
    `${typeName},Mid,Onshore,Vendor,95,,Engineering`
  ].join('\n');
  await page.locator('input[name="File"]').setInputFiles({ name: 'rates.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });
  await page.getByRole('button', { name: 'Import', exact: true }).click();
  await expect(page.getByText('Import rejected')).toHaveCount(0);
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(page.locator('.alert-info')).toContainText('This card is published');

  const id = await createInitiative(page, uniqueName('Vendor options'));
  await addPhase(page, 'Future build', isoDate(3651), isoDate(3652));
  const addUrl = `/Initiatives/AddAllocation/${id}`;
  await page.goto(addUrl);
  await page.locator('#ResourceTypeId').selectOption({ label: typeName });
  await page.locator('#SeniorityId').selectOption({ label: 'Senior' });
  await page.locator('#Location').selectOption('Onshore');
  await page.locator('#ResourcingClassId').selectOption({ label: 'Internal' });
  await page.locator('#EstimatedHours').fill('8');
  await page.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/Initiatives/Details/${id}`));
  const editUrl = await page.locator('#allocations-table').getByRole('link', { name: 'Edit', exact: true }).getAttribute('href');

  for (const url of [addUrl, editUrl!]) {
    await page.goto(url);
    await page.locator('#ResourceTypeId').selectOption({ label: typeName });
    await page.locator('#SeniorityId').selectOption({ label: 'Senior' });
    await page.locator('#Location').selectOption('Onshore');
    await page.locator('#ResourcingClassId').selectOption({ label: 'Vendor' });
    await expect(page.locator('#VendorId option:checked')).toHaveText(vendorName);
    await expect(page.locator('#rate-preview')).toHaveValue('$125.00 /h');

    await page.locator('#Location').selectOption('Offshore');
    await expect(page.locator('#ResourcingClassId option', { hasText: /^Vendor$/ })).toBeDisabled();
    await expect(page.locator('#ResourcingClassId option:checked')).toHaveText('Internal');
    await expect(page.locator('#VendorId')).toBeHidden();
    await expect(page.locator('#rate-preview')).toHaveValue('$80.00 /h');

    await page.locator('#SeniorityId').selectOption({ label: 'Mid' });
    await page.locator('#ResourcingClassId').selectOption({ label: 'Vendor' });
    await expect(page.locator('#VendorId option:not([disabled])')).not.toHaveCount(0);
    await expect(page.locator('#rate-preview')).toHaveValue('$95.00 /h');
  }

  await page.goto(vendorEdit!);
  await page.locator('#IsActive').uncheck();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  for (const url of [addUrl, editUrl!]) {
    await page.goto(url);
    await page.locator('#ResourceTypeId').selectOption({ label: typeName });
    await page.locator('#SeniorityId').selectOption({ label: 'Senior' });
    await page.locator('#Location').selectOption('Onshore');
    await expect(page.locator('#ResourcingClassId option', { hasText: /^Vendor$/ })).toBeDisabled();
    await expect(page.locator('#VendorId')).toBeHidden();
    await expect(page.locator('#rate-preview')).toHaveValue('$100.00 /h');
  }

  await page.goto(vendorEdit!);
  await page.locator('#IsActive').check();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await page.goto(editUrl!);
  await page.locator('#ResourcingClassId').selectOption({ label: 'Vendor' });
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/Initiatives/Details/${id}`));
  await expect(page.locator('#allocations-table')).toContainText(vendorName);

  await page.goto(editUrl!);
  const savedClass = await page.locator('#ResourcingClassId').inputValue();
  const savedVendor = await page.locator('#VendorId').inputValue();
  await expect(page.locator('#CapexPercent')).toHaveValue('100');
  await page.goto(cardUrl);
  await page.locator('#retire-end').fill(isoDate(3650));
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Retire', exact: true }).click();
  await expect(page.locator('.alert-secondary')).toContainText('This card is retired');

  for (const inactive of [false, true]) {
    if (inactive) {
      await page.goto(vendorEdit!);
      await page.locator('#IsActive').uncheck();
      await page.getByRole('button', { name: 'Save', exact: true }).click();
    }
    await page.goto(editUrl!);
    await expect(page.locator('#ResourcingClassId')).toHaveValue(savedClass);
    await expect(page.locator('#VendorId')).toHaveValue(savedVendor);
    await expect(page.locator('#VendorId option:checked')).toBeEnabled();
    await expect(page.locator('#CapexPercent')).toHaveValue('100');
    await expect(page.locator('#rate-preview')).toHaveClass(/is-invalid/);
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`/Initiatives/Details/${id}`));
    await page.goto(editUrl!);
    await expect(page.locator('#ResourcingClassId')).toHaveValue(savedClass);
    await expect(page.locator('#VendorId')).toHaveValue(savedVendor);
    await expect(page.locator('#CapexPercent')).toHaveValue('100');
  }
});
