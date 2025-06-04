export class List<T> extends Array<T> {
    // Add an item to the list
    public add(item: T): void {
        this.push(item);
    }

    // Add multiple items to the list
    public addRange(items: T[]): void {
        this.push(...items);
    }

    // Remove an item from the list (first occurrence)
    public remove(item: T): boolean {
        const index = this.indexOf(item);
        if (index !== -1) {
            this.splice(index, 1);
            return true;
        }
        return false;
    }

    // Remove an item at a specific index
    public removeAt(index: number): void {
        if (index >= 0 && index < this.length) {
            this.splice(index, 1);
        } else {
            throw new Error("Index out of range.");
        }
    }

    // Clear the list
    public clear(): void {
        this.length = 0;
        //this.items = [];
    }

    // Check if the list contains an item
    public contains(item: T): boolean {
        return this.indexOf(item) !== -1;
    }

    // Get the item at a specific index
    public get(index: number): T {
        if (index >= 0 && index < this.length) {
            return this[index];
        } else {
            throw new Error("Index out of range.");
        }
    }

    // Find all items that match a predicate
    public findAll(predicate: (item: T) => boolean): T[] {
        return this.filter(predicate);
    }

    // Get the index of an item
    public indexOf(item: T): number {
        return this.indexOf(item);
    }

    // Get the number of items in the list
    public get count(): number {
        return this.length;
    }

    // Get all items in the list as an array
    public toArray(): T[] {
        return [...this];
    }

    // Insert an item at a specific index
    public insert(index: number, item: T): void {
        if (index >= 0 && index <= this.length) {
            this.splice(index, 0, item);
        } else {
            throw new Error("Index out of range.");
        }
    }
}
