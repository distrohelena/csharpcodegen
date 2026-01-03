using System;

namespace cs2.go {
    /// <summary>
    /// Registers runtime requirement definitions with a Go program.
    /// </summary>
    public class GoRuntimeRequirementRegistrar {
        /// <summary>
        /// Adds runtime requirements to the program.
        /// </summary>
        /// <param name="program">The program receiving the requirements.</param>
        public void Register(GoProgram program) {
            if (program == null) {
                throw new ArgumentNullException(nameof(program));
            }

            foreach (GoRuntimeRequirementDefinition requirement in GoRuntimeRequirementCatalog.BaseRequirements) {
                program.AddRequirement(requirement.CreateKnownClass());
                if (!string.IsNullOrWhiteSpace(requirement.ImportPath)) {
                    program.RegisterPackageImport(requirement.ImportPath, requirement.Alias);
                    program.RegisterTypeImport(requirement.Name, requirement.ImportPath, requirement.Alias);
                }
            }
        }
    }
}
