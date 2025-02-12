import { WebSocketState } from "./websocket-state";

export class WebSocketWS {
    private webSocket: WebSocket;

    public OnOpen: (sender, e) => void;
    public OnClose: (sender, e) => void;
    public OnMessage: (sender, message) => void;
    public OnError: (sender, message) => void;

    private state: WebSocketState;
    get readyState(): WebSocketState {
        return this.state;
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
                this.OnOpen(event, null);
            }
        }

        this.webSocket.onclose = (event) => {
            if (this.OnClose) {
                this.OnClose(event, {
                    Reason: event.reason
                });
            }
        }

        this.webSocket.onerror = (event) => {
        }

        this.webSocket.onmessage = (event) => {
            const arrBuffer = event.data as ArrayBuffer; // Directly get the ArrayBuffer
            const uint8Array = new Uint8Array(arrBuffer); // Convert to Uint8Array if needed
            this.OnMessage(null, {
                RawData: uint8Array
            });
        }

    }

    public Close() {
        this.webSocket.close();
    }

    public Send(buffer: Buffer) {
        if (this.state != WebSocketState.Open) {
            return;
        }

        this.webSocket.send(buffer);
    }
}