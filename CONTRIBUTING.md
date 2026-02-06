# Contributing to Microbot

First off, thank you for considering contributing to Microbot! It's people like you that make Microbot such a great tool.

## Code of Conduct

This project and everyone participating in it is governed by our commitment to creating a welcoming and inclusive environment. By participating, you are expected to uphold this standard. Please report unacceptable behavior to the project maintainers.

## How Can I Contribute?

### Reporting Bugs

Before creating bug reports, please check the existing issues to avoid duplicates. When you create a bug report, include as many details as possible:

- **Use a clear and descriptive title**
- **Describe the exact steps to reproduce the problem**
- **Provide specific examples** (configuration snippets, error messages)
- **Describe the behavior you observed and what you expected**
- **Include your environment details** (.NET version, OS, AI provider)

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When creating an enhancement suggestion:

- **Use a clear and descriptive title**
- **Provide a detailed description of the proposed functionality**
- **Explain why this enhancement would be useful**
- **List any alternatives you've considered**

### Pull Requests

1. **Fork the repository** and create your branch from `main`
2. **Follow the coding style** of the project
3. **Add tests** if applicable
4. **Update documentation** as needed
5. **Ensure the test suite passes**
6. **Write a clear commit message**

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An IDE (Visual Studio 2022, VS Code with C# extension, or JetBrains Rider)
- Git

### Getting Started

```bash
# Clone your fork
git clone https://github.com/your-username/Microbot.git
cd Microbot

# Add upstream remote
git remote add upstream https://github.com/original-owner/Microbot.git

# Create a feature branch
git checkout -b feature/your-feature-name

# Build the project
dotnet build

# Run tests
dotnet test
```

### Project Structure

See the [AGENTS.md](AGENTS.md) file for detailed information about the project structure and architecture.

## Coding Guidelines

### C# Style

- Follow [Microsoft's C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful names for variables, methods, and classes
- Add XML documentation comments for public APIs
- Keep methods focused and small
- Use async/await for I/O operations

### Commit Messages

- Use the present tense ("Add feature" not "Added feature")
- Use the imperative mood ("Move cursor to..." not "Moves cursor to...")
- Limit the first line to 72 characters or less
- Reference issues and pull requests when relevant

Example:
```
Add YouTrack skill for issue management

- Implement issue CRUD operations
- Add comment management
- Support project listing
- Include permission modes (ReadOnly, FullControl)

Closes #123
```

### Documentation

- Update README.md if you change user-facing functionality
- Update AGENTS.md if you change architecture or add new components
- Add inline comments for complex logic
- Include XML documentation for public APIs

## Adding New Skills

When adding a new skill:

1. Create a new project: `src/Microbot.Skills.{SkillName}/`
2. Implement the skill class with `[KernelFunction]` attributes
3. Create a loader in `src/Microbot.Skills/Loaders/`
4. Add configuration model in `src/Microbot.Core/Models/MicrobotConfig.cs`
5. Update the configuration wizard in `Program.cs`
6. Create an implementation plan in `plans/{skill-name}-skill-implementation.md`
7. Update AGENTS.md with the new skill status

## Testing

- Write unit tests for new functionality
- Ensure existing tests pass before submitting PR
- Test with different AI providers if applicable
- Test edge cases and error handling

## Questions?

Feel free to open an issue with the "question" label if you have any questions about contributing.

## Recognition

Contributors will be recognized in the project. Thank you for helping make Microbot better!
