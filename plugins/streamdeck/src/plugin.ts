import streamDeck, { action, SingletonAction, type Action, type WillAppearEvent, type WillDisappearEvent,
  type DidReceiveSettingsEvent, type KeyDownEvent, type SendToPluginEvent, type PropertyInspectorDidAppearEvent } from '@elgato/streamdeck';
import { Bridge } from './bridge.mjs';
import { keyImage } from './render.mjs';

type Settings = { serviceId?: string; language?: string; minutes?: number };
type Kind = 'service' | 'global' | 'refresh' | 'snooze';
const bridge = new Bridge();

class LookoutAction extends SingletonAction<Settings> {
  private visible = new Map<string, { action: Action<Settings>; settings: Settings; image?: string }>();
  private inspector?: Action<Settings>;
  private inspectorPayload = '';
  constructor(private kind: Kind) {
    super();
    bridge.listeners.add(() => { void this.renderAll(); if (this.inspector) void this.sendServices(); });
  }
  override async onWillAppear(ev: WillAppearEvent<Settings>) {
    this.visible.set(ev.action.id, { action: ev.action, settings: ev.payload.settings });
    await this.renderAll(); await bridge.poll();
  }
  override onWillDisappear(ev: WillDisappearEvent<Settings>) { this.visible.delete(ev.action.id); }
  override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<Settings>) {
    const entry = this.visible.get(ev.action.id); if (entry) entry.settings = ev.payload.settings;
    await this.renderAll(); if (this.inspector) await this.sendServices();
  }
  private async renderAll() {
    for (const entry of this.visible.values()) {
      if (!entry.action.isKey()) continue;
      const image = keyImage(this.kind, entry.settings, bridge.snapshot);
      if (entry.image === image) continue;
      try { await entry.action.setImage(`data:image/svg+xml;base64,${Buffer.from(image).toString('base64')}`); await entry.action.setTitle(''); entry.image = image; }
      catch { streamDeck.logger.warn('A Stream Deck key could not be updated.'); }
    }
  }
  override async onKeyDown(ev: KeyDownEvent<Settings>) {
    try {
      if (this.kind === 'global') await bridge.command('openDashboard');
      else if (this.kind === 'refresh') await bridge.command('refresh');
      else {
        const id = ev.payload.settings.serviceId;
        if (!id) throw new Error('Choose a service in the key settings.');
        if (this.kind === 'service') await bridge.command('openService', { serviceId: id });
        else {
          await bridge.poll();
          const service = bridge.snapshot?.services.find((s: {id: string}) => s.id === id);
          if (!service) throw new Error('The service is not monitored.');
          const minutes = Date.parse(service.snoozedUntil ?? '') > Date.now() ? 0 : ev.payload.settings.minutes === 60 ? 60 : 30;
          await bridge.command('snooze', { serviceId: id, minutes });
        }
      }
      streamDeck.logger.info(`Action completed: ${this.kind}`);
    } catch (error) { streamDeck.logger.warn(error instanceof Error ? error.message : 'Action failed.'); if (ev.action.isKey()) await ev.action.showAlert(); }
  }
  override async onPropertyInspectorDidAppear(ev: PropertyInspectorDidAppearEvent<Settings>) { this.inspector = ev.action; this.inspectorPayload = ''; await bridge.poll(); await this.sendServices(); }
  override onPropertyInspectorDidDisappear() { this.inspector = undefined; this.inspectorPayload = ''; }
  override async onSendToPlugin(ev: SendToPluginEvent<{ event?: string }, Settings>) {
    const payload = ev.payload as { event?: string };
    if (payload?.event === 'getServices') { this.inspectorPayload = ''; await bridge.poll(); await this.sendServices(); }
    else if (payload?.event === 'openDashboard') {
      try { await bridge.command('openDashboard'); } catch { await this.sendServices(); }
    }
  }
  private async sendServices() {
    if (!this.inspector) return;
    const snapshot = bridge.snapshot;
    const payload = { event: 'services', connected: !!snapshot, kind: this.kind,
      language: snapshot?.language ?? 'en', demo: snapshot?.isDemo ?? false,
      services: snapshot?.services.map((s: {id: string; name: string}) => ({id: s.id, name: s.name})) ?? [] };
    const serialized = JSON.stringify(payload); if (serialized === this.inspectorPayload) return;
    try { await streamDeck.ui.sendToPropertyInspector(payload); this.inspectorPayload = serialized; }
    catch { streamDeck.logger.warn('The property inspector is unavailable.'); }
  }
}
@action({ UUID: 'com.bouneh.statuslookout.service' })
class ServiceAction extends LookoutAction { constructor() { super('service'); } }
@action({ UUID: 'com.bouneh.statuslookout.global' })
class GlobalAction extends LookoutAction { constructor() { super('global'); } }
@action({ UUID: 'com.bouneh.statuslookout.refresh' })
class RefreshAction extends LookoutAction { constructor() { super('refresh'); } }
@action({ UUID: 'com.bouneh.statuslookout.snooze' })
class SnoozeAction extends LookoutAction { constructor() { super('snooze'); } }
streamDeck.actions.registerAction(new ServiceAction()); streamDeck.actions.registerAction(new GlobalAction());
streamDeck.actions.registerAction(new RefreshAction()); streamDeck.actions.registerAction(new SnoozeAction());
await streamDeck.connect();
streamDeck.logger.info('StatusLookout plugin connected to Stream Deck.');
streamDeck.logger.info(`Connected device types: ${streamDeck.devices.map(device => device.type).toArray().join(', ')}`);
setInterval(() => void bridge.poll(), 2000);
await bridge.poll();
