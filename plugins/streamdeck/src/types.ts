export type Status = 'Unknown' | 'Operational' | 'Maintenance' | 'Degraded' | 'Outage';
export type Service = { id: string; name: string; status: Status; message: string; statusUrl: string; isMuted: boolean; snoozedUntil: string | null; logo: string | null };
export type Snapshot = { protocol: number; version: string; language: string; isDemo: boolean; isPaused: boolean; isRefreshing: boolean; overall: Status; services: Service[]; catalog?: {id:string;name:string;category:string;watched:boolean;family?:string}[] };
