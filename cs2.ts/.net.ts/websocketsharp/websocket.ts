// @ts-nocheck
import { WebSocketState } from "./websocket-state";
import { Event } from "../system/event";

export class WebSocketWS {
    private webSocket: WebSocket;
    private _address: string;

    public OnOpen: Event = new Event()
    public OnClose: Event = new Event()
    public OnMessage: Event = new Event()
    public OnError: Event = new Event();

    private state: WebSocketState;
    get readyState(): WebSocketState {
        return this.state;
    }

    get address(): string {
        return this._address;
    }

    private url: string;
    private connecting: boolean;

    constructor(url: string, options: any) {
        this.url = url;
    }

    public Connect() {
        if (this.connecting) {
            return;
        }
        this.connecting = true;

        try {
            this.webSocket = new WebSocket(this.url);
            this.webSocket.binaryType = "arraybuffer";
        } catch {
            console.log("?????");
            return null;
        }

        this.webSocket.onopen = (event) => {
            this.state = WebSocketState.Open;

            if (this.OnOpen) {
                this.OnOpen.Invoke(event, null);
            }
        }

        this.webSocket.onclose = (event) => {
            this.state = WebSocketState.Closed;

            if (this.OnClose) {
                this.OnClose.Invoke(event, {
                    Reason: event.reason
                });
            }
        }

        this.webSocket.onerror = (event) => {
        }

        this.webSocket.onmessage = (event) => {
            const arrBuffer = event.data as ArrayBuffer; // Directly get the ArrayBuffer
            const uint8Array = new Uint8Array(arrBuffer); // Convert to Uint8Array if needed
            this.OnMessage.Invoke(null, {
                rawData: uint8Array
            });
        }

    }

    public Close() {
        this.webSocket.close();
    }

    public Send(buffer: Uint8Array) {
        if (this.state != WebSocketState.Open) {
            return;
        }

        this.webSocket.send(buffer);
    }
}