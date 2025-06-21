export class Guid {
    private readonly byteArray: Uint8Array;

    public constructor(byteArray: Uint8Array) {
        if (byteArray.length !== 16) {
            throw new Error('Invalid byte array length. A GUID must be 16 bytes.');
        }
        this.byteArray = byteArray;
    }

    // Static method to create a new GUID
    public static newGuid(): Guid {
        const byteArray = new Uint8Array(16);
        for (let i = 0; i < 16; i++) {
            byteArray[i] = Math.floor(Math.random() * 256);
        }

        // Set the version to 4 (randomly generated UUID)
        byteArray[6] = (byteArray[6] & 0x0f) | 0x40;
        // Set the variant to 10xxxxxx
        byteArray[8] = (byteArray[8] & 0x3f) | 0x80;

        return new Guid(byteArray);
    }

    // Static method to parse a GUID from a string
    public static parse(value: string): Guid {
        const hexRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
        if (!hexRegex.test(value)) {
            throw new Error('Invalid GUID format');
        }

        const byteArray = new Uint8Array(16);
        const hexString = value.replace(/-/g, '');
        for (let i = 0; i < 16; i++) {
            byteArray[i] = parseInt(hexString.substr(i * 2, 2), 16);
        }

        // Fix endianness
        byteArray[0] = parseInt(value.substring(6, 8), 16);
        byteArray[1] = parseInt(value.substring(4, 6), 16);
        byteArray[2] = parseInt(value.substring(2, 4), 16);
        byteArray[3] = parseInt(value.substring(0, 2), 16);
        byteArray[4] = parseInt(value.substring(11, 13), 16);
        byteArray[5] = parseInt(value.substring(9, 11), 16);
        byteArray[6] = parseInt(value.substring(16, 18), 16);
        byteArray[7] = parseInt(value.substring(14, 16), 16);

        return new Guid(byteArray);
    }

    // Constructor to create a GUID from a byte array
    public static fromByteArray(byteArray: Uint8Array): Guid {
        return new Guid(byteArray);
    }

    // Method to check if another GUID is equal to the current one
    public equals(other: Guid): boolean {
        return this.byteArray.every((byte, index) => byte === other.byteArray[index]);
    }

    // Method to return the string representation of the GUID
    public toString(): string {
        const hexPairs = Array.from(this.byteArray).map(byte => byte.toString(16).padStart(2, '0'));
        return `${hexPairs.slice(3, 4).join('')}${hexPairs.slice(2, 3).join('')}${hexPairs.slice(1, 2).join('')}${hexPairs.slice(0, 1).join('')}-${hexPairs.slice(5, 6).join('')}${hexPairs.slice(4, 5).join('')}-${hexPairs.slice(7, 8).join('')}${hexPairs.slice(6, 7).join('')}-${hexPairs.slice(8, 10).join('')}-${hexPairs.slice(10).join('')}`;
    }

    // Static method to check if a string is a valid GUID
    public static isValid(value: string): boolean {
        const hexRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
        return hexRegex.test(value);
    }

    // Method to return the byte array representation of the GUID
    public toByteArray(): Uint8Array {
        return this.byteArray;
    }
}
