using System.Collections.Generic;
using System;

namespace cs2.ts {
    /// <summary>
    /// Registers runtime requirement definitions with a TypeScript program.
    /// </summary>
    public class TypeScriptRuntimeRequirementRegistrar {
        /// <summary>
        /// Adds runtime requirements for the given environment to the program.
        /// </summary>
        /// <param name="program">The program receiving the requirements.</param>
        /// <param name="env">The runtime environment to target.</param>
        public void Register(TypeScriptProgram program, TypeScriptEnvironment env) {
            if (program == null) {
                throw new ArgumentNullException(nameof(program));
            }

            IEnumerable<TypeScriptRuntimeRequirementDefinition> requirements = TypeScriptRuntimeRequirementCatalog.GetRequirements(env);
            foreach (TypeScriptRuntimeRequirementDefinition requirement in requirements) {
                program.AddRequirement(requirement.CreateKnownClass());
            }
        }
    }
}
