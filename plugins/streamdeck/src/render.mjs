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
    detail: snapshot.isRefreshing ? text.checking : snapshot.isPaused ? text.paused : language(settings, snapshot) === 'fr' ? 'Maintenant' : 'Check now', symbol: 'refresh', demo };
  if (!service) return { color: colors.Unknown, name: text[kind === 'snooze' ? 'snooze' : 'choose'], detail: settings.serviceId ? text.missing : 'StatusLookout', symbol: 'radar', demo };
  const until = Date.parse(service.snoozedUntil ?? ''); const snoozed = until > now;
  const detail = snapshot.isPaused ? text.paused : service.isMuted ? text.muted : snoozed ? `${Math.ceil((until - now)/60000)} min` : kind === 'snooze' ? `${settings.minutes === 60 ? 60 : 30} min` : text[service.status] ?? text.Unknown;
  return { color: snapshot.isPaused ? colors.Unknown : colors[service.status] ?? colors.Unknown, name: service.name, detail,
    logo: service.logo?.startsWith('data:image/png;base64,') ? service.logo : undefined, symbol: kind === 'snooze' ? 'snooze' : 'radar', demo };
}
export function keyImage(kind, settings, snapshot) {
  const v = view(kind, settings, snapshot);
  // Keep type large at the device's 72px resolution; shorten long labels instead of shrinking them indefinitely.
  const fit = (value, preferred, minimum) => {
    const letters = Array.from(String(value));
    const units = text => Array.from(text).reduce((sum, c) => sum + (/[MW@%]/.test(c) ? .95 : /[ilI1 !.,:;'|]/.test(c) ? .32 : /[A-Z0-9]/.test(c) ? .68 : .59), 0);
    const size = Math.max(minimum, Math.min(preferred, Math.floor(122 / Math.max(1, units(value)))));
    if (units(letters.join('')) * size > 122) {
      while (letters.length > 1 && units(letters.join('') + '…') * size > 122) letters.pop();
      letters.push('…');
    }
    return { text: letters.join(''), size };
  };
  const name = fit(v.name, 24, 20), detail = fit(v.detail, 21, 18), count = fit(v.value ?? '', 68, 32);
  const center = v.logo ? `<rect x="41" y="11" width="62" height="62" rx="12" fill="#FFFFFF"/><image href="${escapeXml(v.logo)}" x="46" y="16" width="52" height="52"/>`
    : v.value ? `<text x="72" y="73" font-size="${count.size}" font-weight="800" fill="${v.color}">${escapeXml(count.text)}</text>`
    : v.symbol === 'refresh' ? `<path d="M92 27a29 29 0 1 0 8 31M94 14v19H75" fill="none" stroke="${v.color}" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>`
    : v.symbol === 'snooze' ? `<path d="M62 17v48m20-48v48" stroke="${v.color}" stroke-width="10" stroke-linecap="round"/>`
    : `<circle cx="72" cy="42" r="28" fill="none" stroke="${v.color}" stroke-width="4"/><circle cx="72" cy="42" r="8" fill="${v.color}"/><path d="M72 42l21-21" stroke="${v.color}" stroke-width="4"/>`;
  const snooze = kind === 'snooze' && v.logo ? `<circle cx="105" cy="65" r="12" fill="${v.color}" stroke="#080C14" stroke-width="2"/><path d="M101 60v10m8-10v10" stroke="#080C14" stroke-width="3"/>` : '';
  return `<svg xmlns="http://www.w3.org/2000/svg" width="144" height="144" viewBox="0 0 144 144"><defs><clipPath id="safe"><rect x="9" y="9" width="126" height="126" rx="8"/></clipPath></defs><rect width="144" height="144" rx="14" fill="#080C14"/><rect x="3" y="3" width="138" height="138" rx="12" fill="none" stroke="${v.color}" stroke-width="4"/><g clip-path="url(#safe)" font-family="Segoe UI,Arial,sans-serif" text-anchor="middle">${center}${snooze}<text x="72" y="99" fill="#FFFFFF" font-size="${name.size}" font-weight="700">${escapeXml(name.text)}</text><text x="72" y="127" fill="${v.color}" font-size="${detail.size}" font-weight="700">${escapeXml(detail.text)}</text>${v.demo ? '<text x="132" y="17" text-anchor="end" fill="#FFBD69" font-size="8">DEMO</text>' : ''}</g></svg>`;
}
