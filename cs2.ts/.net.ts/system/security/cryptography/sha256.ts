export class SHA256 {
    static Create(): SHA256 {
        return null as any;
    }

    static async hashData(data: Uint8Array): Promise<Uint8Array> {
        const hashBuffer = await crypto.subtle.digest('SHA-256', data);
        return new Uint8Array(hashBuffer);
    }
}