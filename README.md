# Crewboss Game

A 16-bit style mobile game built with C#, .NET, and MonoGame.

## Project Structure

- `src/` - The main game project
  - `Game1.cs` - The main game class
  - `GameManager.cs` - Manages game state
  - `Screens/` - Contains different game screens
    - `Screen.cs` - Base class for all screens
    - `MainMenuScreen.cs` - Main menu screen
    - `ScreenManager.cs` - Handles screen transitions
  - `Content/` - Game assets
    - `Menu-images/` - Menu and UI images

## Getting Started

### Prerequisites

- .NET SDK 8.0 or later
- MonoGame framework

### Building and Running

To build the game:

```bash
cd src
dotnet build
```

To run the game:

```bash
cd src
dotnet run
```

## Game Architecture

The game uses a screen-based architecture:

1. `Game1` - Main entry point that initializes the game and manages the game loop
2. `ScreenManager` - Manages different screens (menu, gameplay, etc.)
3. `GameManager` - Handles the overall game state

When the game starts up, it displays the main menu screen with the game title.

## Adding New Screens

To add a new screen:

1. Create a new class that inherits from `Screen`
2. Implement the required methods: `LoadContent()`, `Update()`, and `Draw()`
3. Register the screen in the `ScreenManager.Initialize()` method

## Adding New Content

To add new game assets:

1. Place the asset files in the appropriate subdirectory of `Content/`
2. Update the `Content.mgcb` file to include the new assets
3. Build the project to process the assets 