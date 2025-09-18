// @ts-nocheck
export enum WebSocketState {
    /// <summary>
    /// Equivalent to numeric value 0. Indicates that a new interface has
    /// been created.
    /// </summary>
    New = 0,
    /// <summary>
    /// Equivalent to numeric value 1. Indicates that the connect process is
    /// in progress.
    /// </summary>
    Connecting = 1,
    /// <summary>
    /// Equivalent to numeric value 2. Indicates that the connection has
    /// been established and the communication is possible.
    /// </summary>
    Open = 2,
    /// <summary>
    /// Equivalent to numeric value 3. Indicates that the close process is
    /// in progress.
    /// </summary>
    Closing = 3,
    /// <summary>
    /// Equivalent to numeric value 4. Indicates that the connection has
    /// been closed or could not be established.
    /// </summary>
    Closed = 4
}