import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { createInitiative, addPhase, isoDate } from './helpers';

/**
 * axe-core gate: every listed page must have zero WCAG 2.x A/AA violations.
 * Rules that cannot be judged automatically or that Bootstrap triggers by design are excluded below.
 */
const disabledRules = [
  'color-contrast' // covered by the Lighthouse accessibility gate; axe flags Bootstrap's muted text at AA threshold edges
];

async function scan(page: import('@playwright/test').Page, label: string) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .disableRules(disabledRules)
    .analyze();
  const summary = results.violations.map(v =>
    `${v.id} (${v.impact}): ${v.help}\n` + v.nodes.slice(0, 3).map(n => `    ${n.target.join(' ')}`).join('\n'));
  expect(summary, `${label} has accessibility violations`).toEqual([]);
}

const staticPages: [string, string][] = [
  ['Home', '/'],
  ['Initiatives list', '/Initiatives'],
  ['New initiative', '/Initiatives/Create'],
  ['Portfolio', '/Portfolio'],
  ['Capacity', '/Capacity'],
  ['Actuals', '/Actuals'],
  ['Audit', '/Audit'],
  ['Help', '/Home/Help'],
  ['Admin users', '/Admin/Users'],
  ['Admin rate cards', '/Admin/RateCards'],
  ['Admin people', '/Admin/People'],
  ['Admin resource types', '/Admin/ResourceTypes']
];

test.describe('Accessibility (axe-core)', () => {
  for (const [label, url] of staticPages) {
    test(`${label} has no WCAG A/AA violations`, async ({ page }) => {
      const response = await page.goto(url);
      expect(response?.ok(), `${url} returned ${response?.status()}`).toBeTruthy();
      await scan(page, label);
    });
  }

  test('Initiative details (Plan, Team, Costs tabs) and Scenarios have no violations', async ({ page }) => {
    const id = await createInitiative(page, `A11y ${Date.now().toString(36)}`);
    await addPhase(page, 'Build', isoDate(0), isoDate(60));
    await scan(page, 'Details / Plan');
    for (const tab of ['Costs', 'Team', 'History']) {
      await page.getByRole('tab', { name: new RegExp(tab) }).click();
      await scan(page, `Details / ${tab}`);
    }
    await page.goto(`/Initiatives/${id}/Scenarios`);
    await scan(page, 'Scenarios compare');
  });
});
