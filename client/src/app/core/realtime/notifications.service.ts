import { Injectable, effect, inject } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { AuthService } from '../auth/auth.service';

@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;
  private connecting = false;

  constructor() {
    effect(() => {
      const token = this.auth.currentToken();
      if (!token) {
        void this.stop();
        return;
      }

      void this.start();
    });
  }

  private async start(): Promise<void> {
    if (this.connection || this.connecting) {
      return;
    }

    this.connecting = true;
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', {
        accessTokenFactory: () => this.auth.currentToken() ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('PermissionsChanged', () => this.refreshProfile());
    connection.onreconnected(() => this.refreshProfile());

    try {
      await connection.start();

      if (!this.auth.currentToken()) {
        await connection.stop();
        return;
      }

      this.connection = connection;
    } catch {
      await connection.stop();
    } finally {
      this.connecting = false;
    }
  }

  private async stop(): Promise<void> {
    const connection = this.connection;
    this.connection = null;

    if (connection && connection.state !== HubConnectionState.Disconnected) {
      await connection.stop().catch(() => void 0);
    }
  }

  private refreshProfile(): void {
    this.auth.reloadProfile().subscribe({ error: () => void 0 });
  }
}
