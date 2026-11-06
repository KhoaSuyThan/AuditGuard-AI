import * as signalR from '@microsoft/signalr';
import { AuditSessionDto, ProgressState } from '../types/audit';

class SignalRService {
  private connection: signalR.HubConnection | null = null;
  private currentSessionId: string | null = null;

  public async connect(): Promise<signalR.HubConnection> {
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      return this.connection;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/audit', {
        skipNegotiation: false,
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Information)
      .build();

    await this.connection.start();
    return this.connection;
  }

  public async joinSession(sessionId: string): Promise<void> {
    const conn = await this.connect();
    if (this.currentSessionId && this.currentSessionId !== sessionId) {
      await conn.invoke('LeaveSession', this.currentSessionId).catch(() => {});
    }
    this.currentSessionId = sessionId;
    await conn.invoke('JoinSession', sessionId);
  }

  public onProgress(callback: (progress: ProgressState) => void): void {
    if (!this.connection) return;
    this.connection.on('ReceiveProgress', (stage: string, percent: number, message: string) => {
      callback({
        stage,
        percent,
        message,
        isProcessing: percent < 100 && stage !== 'Failed',
        error: stage === 'Failed' ? message : undefined
      });
    });
  }

  public onAuditCompleted(callback: (session: AuditSessionDto) => void): void {
    if (!this.connection) return;
    this.connection.on('ReceiveAuditCompleted', (session: AuditSessionDto) => {
      // Lưu vào localStorage để khôi phục trọn vẹn cả ComparisonRows khi reload
      if (session && session.id) {
        localStorage.setItem(`audit_session_${session.id}`, JSON.stringify(session));
      }
      callback(session);
    });
  }

  public disconnect(): void {
    if (this.connection) {
      this.connection.stop();
      this.connection = null;
      this.currentSessionId = null;
    }
  }
}

export const signalRService = new SignalRService();
