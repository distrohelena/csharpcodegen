import type { Func, Func2 } from '../../cs2.ts/.net.ts/system/func';
import type { Task } from '../../cs2.ts/.net.ts/system/threading/tasks/task';

// C# Func<T> returns T synchronously; its invocation can feed an immediate scalar API.
const next: Func<number> = () => 7;
const immediate: number = next();

// A C# Func<Task<T>> remains async because Task<T> itself maps to Promise<T>.
const fetchLength: Func2<string, Task<number>> = async value => value.length;
const pending: Promise<number> = fetchLength('value');

void immediate;
void pending;