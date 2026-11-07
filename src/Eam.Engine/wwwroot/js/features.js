// Screens of steps 6 to 9, plugged into a server's page.
import { worldTabs, renderWorld, worldEvent } from "./world.js";
import { communityTabs, renderCommunity, communityEvent } from "./community.js";
import { securityTabs, renderSecurity } from "./security.js";
import { extraTabs, renderExtra } from "./extras.js";

const modules = [
  { tabs: worldTabs, render: renderWorld, event: worldEvent },
  { tabs: communityTabs, render: renderCommunity, event: communityEvent },
  { tabs: securityTabs, render: renderSecurity, event: () => false },
  { tabs: extraTabs, render: renderExtra, event: () => false },
];

export const featureTabs = (role) => modules.flatMap((m) => m.tabs(role));
export const isFeatureTab = (tab, role) => featureTabs(role).includes(tab);
export function renderFeature(tab, el, s, me) {
  const m = modules.find((x) => x.tabs(me.role).includes(tab));
  return m?.render(tab, el, s, me);
}
export function featureEvent(msg) { return modules.some((m) => m.event(msg)); }
