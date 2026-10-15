#pragma once

/// <summary>Retains a known loaded code module; an empty ID denotes resident code.</summary>
void HeCppRetainCodeModule(const char* moduleId);

/// <summary>Releases a module pin without unloading code during the caller's execution.</summary>
void HeCppReleaseCodeModule(const char* moduleId);

/// <summary>Keeps code mapped while an object or escaped callable can still execute it.</summary>
class HeCppCodeModulePin {
    /// <summary>Identifies the pinned module; null only represents a transferred pin.</summary>
    const char* ModuleId;

public:
    /// <summary>Acquires the module before publishing an object or callable that uses it.</summary>
    explicit HeCppCodeModulePin(const char* moduleId) : ModuleId(moduleId) {
        HeCppRetainCodeModule(ModuleId);
    }

    /// <summary>Acquires an independent pin for a copied owner.</summary>
    HeCppCodeModulePin(const HeCppCodeModulePin& other) : ModuleId(other.ModuleId) {
        if (ModuleId != nullptr) {
            HeCppRetainCodeModule(ModuleId);
        }
    }

    /// <summary>Transfers ownership without temporarily releasing the live module.</summary>
    HeCppCodeModulePin(HeCppCodeModulePin&& other) noexcept : ModuleId(other.ModuleId) {
        other.ModuleId = nullptr;
    }

    /// <summary>Retains the replacement before releasing the previous owner.</summary>
    HeCppCodeModulePin& operator=(const HeCppCodeModulePin& other) {
        if (this != &other) {
            if (other.ModuleId != nullptr) {
                HeCppRetainCodeModule(other.ModuleId);
            }
            if (ModuleId != nullptr) {
                HeCppReleaseCodeModule(ModuleId);
            }
            ModuleId = other.ModuleId;
        }
        return *this;
    }

    /// <summary>Transfers the replacement pin and retires the previous owner.</summary>
    HeCppCodeModulePin& operator=(HeCppCodeModulePin&& other) noexcept {
        if (this != &other) {
            if (ModuleId != nullptr) {
                HeCppReleaseCodeModule(ModuleId);
            }
            ModuleId = other.ModuleId;
            other.ModuleId = nullptr;
        }
        return *this;
    }

    /// <summary>Releases the owner; the backend separately decides when unloading is safe.</summary>
    ~HeCppCodeModulePin() {
        if (ModuleId != nullptr) {
            HeCppReleaseCodeModule(ModuleId);
        }
    }
};
