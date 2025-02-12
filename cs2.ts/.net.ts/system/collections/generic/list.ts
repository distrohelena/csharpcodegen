export class List<T> extends Array<T> {
    // Add an item to the list
    public Add(item: T): void {
        this.push(item);
    }

    // Add multiple items to the list
    public AddRange(items: T[]): void {
        this.push(...items);
    }

    // Remove an item from the list (first occurrence)
    public Remove(item: T): boolean {
        const index = this.indexOf(item);
        if (index !== -1) {
            this.splice(index, 1);
            return true;
        }
        return false;
    }

    // Remove an item at a specific index
    public RemoveAt(index: number): void {
        if (index >= 0 && index < this.length) {
            this.splice(index, 1);
        } else {
            throw new Error("Index out of range.");
        }
    }

    // Clear the list
    public Clear(): void {
        this.length = 0;
        //this.items = [];
    }

    // Check if the list contains an item
    public Contains(item: T): boolean {
        return this.indexOf(item) !== -1;
    }

    // Get the item at a specific index
    public Get(index: number): T {
        if (index >= 0 && index < this.length) {
            return this[index];
        } else {
            throw new Error("Index out of range.");
        }
    }

    // Find the first item that matches a predicate
    public Find(predicate: (item: T) => boolean): T | undefined {
        return this.find(predicate);
    }

    // Find all items that match a predicate
    public FindAll(predicate: (item: T) => boolean): T[] {
        return this.filter(predicate);
    }

    // Get the index of an item
    public IndexOf(item: T): number {
        return this.indexOf(item);
    }

    // Get the number of items in the list
    public get Count(): number {
        return this.length;
    }

    // Get all items in the list as an array
    public ToArray(): T[] {
        return [...this];
    }

    // Insert an item at a specific index
    public Insert(index: number, item: T): void {
        if (index >= 0 && index <= this.length) {
            this.splice(index, 0, item);
        } else {
            throw new Error("Index out of range.");
        }
    }

    // Sort the list using a compare function
    public Sort(compareFn?: (a: T, b: T) => number): void {
        this.sort(compareFn);
    }

    // Reverse the list
    public Reverse(): void {
        this.reverse();
    }

    // ForEach loop
    public ForEach(callback: (item: T, index: number) => void): void {
        this.forEach(callback);
    }
}
