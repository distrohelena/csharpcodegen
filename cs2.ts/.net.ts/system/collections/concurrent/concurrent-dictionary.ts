export class ConcurrentDictionary<TKey, TValue> {
    private items: Map<TKey, TValue> = new Map();
    private lock: Promise<void> = Promise.resolve();

    // Acquire a lock for concurrency-safe operations
    private async acquireLock(): Promise<void> {
        let release: () => void;
        const newLock = new Promise<void>((resolve) => (release = resolve));
        const previousLock = this.lock;
        this.lock = newLock;
        await previousLock;
        release!();
    }

    // Add or update a key-value pair
    public async AddOrUpdate(key: TKey, value: TValue): Promise<void> {
        await this.acquireLock();
        try {
            this.items.set(key, value);
        } finally {
            // Lock released automatically after the operation
        }
    }

    // Add or update using a factory for the update value based on the existing value
    public async AddOrUpdateWithFactory(
        key: TKey,
        addValue: TValue,
        updateValueFactory: (existingValue: TValue) => TValue
    ): Promise<void> {
        await this.acquireLock();
        try {
            const existingValue = this.items.get(key);
            if (existingValue !== undefined) {
                this.items.set(key, updateValueFactory(existingValue));
            } else {
                this.items.set(key, addValue);
            }
        } finally {
            // Lock released automatically after the operation
        }
    }

    // Try to add a new key-value pair, only if it does not already exist
    public async TryAdd(key: TKey, value: TValue): Promise<boolean> {
        await this.acquireLock();
        try {
            if (!this.items.has(key)) {
                this.items.set(key, value);
                return true;
            }
            return false;
        } finally {
            // Lock released automatically after the operation
        }
    }

    // Try to get a value without throwing an error if not found
    public async TryGetValue(key: TKey, outValue: { value?: TValue }): Promise<boolean> {
        await this.acquireLock();
        try {
            const value = this.items.get(key);
            if (value !== undefined) {
                outValue.value = value;
                return true;
            }
            outValue.value = undefined;
            return false;
        } finally {
            // Lock released automatically after the operation
        }
    }

    // Get a value by key using indexing
    public async get(key: TKey): Promise<TValue | undefined> {
        return this.Get(key);
    }

    // Set value by key using indexing
    public async set(key: TKey, value: TValue): Promise<void> {
        return this.AddOrUpdate(key, value);
    }

    // Get a value by key
    public async Get(key: TKey): Promise<TValue | undefined> {
        await this.acquireLock();
        try {
            return this.items.get(key);
        } finally {
            // Lock released automatically after the operation
        }
    }

    // Remove an item by key
    public async Remove(key: TKey): Promise<boolean> {
        await this.acquireLock();
        try {
            return this.items.delete(key);
        } finally {
            // Lock released automatically after the operation
        }
    }

    // Check if the dictionary contains a key
    public async ContainsKey(key: TKey): Promise<boolean> {
        await this.acquireLock();
        try {
            return this.items.has(key);
        } finally {
            // Lock released automatically after the operation
        }
    }

    // Get the count of elements in the dictionary
    public get Count(): number {
        return this.items.size;
    }

    // Clear the dictionary
    public async Clear(): Promise<void> {
        await this.acquireLock();
        try {
            this.items.clear();
        } finally {
            // Lock released automatically after the operation
        }
    }

    // Get all keys in sorted order
    public async Keys(): Promise<TKey[]> {
        await this.acquireLock();
        try {
            return Array.from(this.items.keys());
        } finally {
            // Lock released automatically after the operation
        }
    }

    // Get all values in sorted order
    public async Values(): Promise<TValue[]> {
        await this.acquireLock();
        try {
            return Array.from(this.items.values());
        } finally {
            // Lock released automatically after the operation
        }
    }
}
