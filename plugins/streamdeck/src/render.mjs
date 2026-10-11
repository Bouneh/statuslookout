const colors = { Operational: '#51DCC1', Degraded: '#FFBD69', Outage: '#FF6F86', Maintenance: '#B6A1FF', Unknown: '#95A4BE' };
const words = {
  en: { disconnected: 'Offline', paused: 'Paused', choose: 'Choose a service', missing: 'Not monitored', Operational: 'Operational', Degraded: 'Degraded', Outage: 'Outage', Maintenance: 'Maintenance', Unknown: 'Unavailable', global: 'Overview', refresh: 'Refresh', checking: 'Checking', snooze: 'Snooze', muted: 'Muted', demo: 'DEMO', services: 'services' },
  fr: { disconnected: 'Déconnecté', paused: 'En pause', choose: 'Choisir un service', missing: 'Non suivi', Operational: 'Opérationnel', Degraded: 'Dégradé', Outage: 'Panne', Maintenance: 'Maintenance', Unknown: 'Indisponible', global: 'Vue globale', refresh: 'Actualiser', checking: 'Vérification', snooze: 'Suspendre', muted: 'Coupé', demo: 'DÉMO', services: 'services' }
};
export function language(settings, snapshot) { return settings.language === 'fr' || settings.language === 'auto' && snapshot?.language === 'fr' ? 'fr' : 'en'; }
export function escapeXml(value) { return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' })[c]); }
export function view(kind, settings, snapshot, now = Date.now()) {
  const text = words[language(settings, snapshot)];
  if (!snapshot) return { color: colors.Unknown, name: 'StatusLookout', detail: text.disconnected, symbol: 'offline', demo: false };
  const service = snapshot.services.find(s => s.id === settings.serviceId);
  const demo = snapshot.isDemo;
  if (kind === 'global') {
    const issues = snapshot.services.filter(s => ['Outage','Degraded','Maintenance'].includes(s.status)).length;
    const unknown = snapshot.services.filter(s => s.status === 'Unknown').length;
    return { color: snapshot.isPaused ? colors.Unknown : colors[snapshot.overall] ?? colors.Unknown, name: text.global,
      detail: snapshot.isPaused ? text.paused : `${issues} ! · ${unknown} ?`, value: `${snapshot.services.length}`, symbol: 'radar', demo };
  }
  if (kind === 'refresh') return { color: snapshot.isPaused ? colors.Unknown : '#82AEFF', name: text.refresh,
    detail: snapshot.isRefreshing ? text.checking : snapshot.isPaused ? text.paused : 'StatusLookout', symbol: 'refresh', demo };
  if (!service) return { color: colors.Unknown, name: text[kind === 'snooze' ? 'snooze' : 'choose'], detail: settings.serviceId ? text.missing : 'StatusLookout', symbol: 'radar', demo };
  const until = Date.parse(service.snoozedUntil ?? ''); const snoozed = until > now;
  const detail = snapshot.isPaused ? text.paused : service.isMuted ? text.muted : snoozed ? `${Math.ceil((until - now)/60000)} min` : kind === 'snooze' ? `${settings.minutes === 60 ? 60 : 30} min` : text[service.status] ?? text.Unknown;
  return { color: snapshot.isPaused ? colors.Unknown : colors[service.status] ?? colors.Unknown, name: service.name, detail,
    logo: service.logo?.startsWith('data:image/png;base64,') ? service.logo : undefined, symbol: kind === 'snooze' ? 'snooze' : 'radar', demo };
}
export function keyImage(kind, settings, snapshot) {
  const v = view(kind, settings, snapshot); const short = value => String(value).length > 20 ? String(value).slice(0, 18) + '…' : value;
  const center = v.logo ? `<rect x="51" y="22" width="42" height="42" rx="10" fill="#F7F9FC"/><image href="${escapeXml(v.logo)}" x="57" y="28" width="30" height="30"/>`
    : v.value ? `<text x="72" y="63" font-size="43" font-weight="700" fill="${v.color}">${escapeXml(v.value)}</text>`
    : v.symbol === 'refresh' ? `<path d="M89 35a23 23 0 1 0 5 24M89 25v16h-16" fill="none" stroke="${v.color}" stroke-width="5" stroke-linecap="round"/>`
    : v.symbol === 'snooze' ? `<path d="M66 22v40m12-40v40" stroke="${v.color}" stroke-width="8" stroke-linecap="round"/>`
    : `<circle cx="72" cy="43" r="22" fill="none" stroke="${v.color}" stroke-width="3"/><circle cx="72" cy="43" r="6" fill="${v.color}"/><path d="M72 43l17-17" stroke="${v.color}" stroke-width="3"/>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="144" height="144" viewBox="0 0 144 144"><rect width="144" height="144" rx="16" fill="#0C1220"/><rect x="5" y="5" width="134" height="134" rx="13" fill="none" stroke="${v.color}" stroke-width="3"/><g font-family="Segoe UI,Arial,sans-serif" text-anchor="middle">${center}<text x="72" y="92" fill="#F1F5FF" font-size="12" font-weight="600">${escapeXml(short(v.name))}</text><text x="72" y="113" fill="${v.color}" font-size="11">${escapeXml(short(v.detail))}</text>${v.demo ? '<text x="72" y="132" fill="#FFBD69" font-size="8">DEMO</text>' : ''}</g></svg>`;
}
