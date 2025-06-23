export class MD5 {
    static Create(): MD5 {
        return new MD5();
    }

    static async hashData(data: Uint8Array): Promise<Uint8Array> {
        const hashBuffer = await crypto.subtle.digest('md5', data);
        return new Uint8Array(hashBuffer);
    }

    async computeHash(data: Uint8Array): Promise<Uint8Array> {
        const hashBuffer = await crypto.subtle.digest('md5', data);
        return new Uint8Array(hashBuffer);
    }
}