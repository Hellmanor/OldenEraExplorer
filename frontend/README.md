# Frontend

React single-page application for browsing and visualizing game data.

## Tech Stack

- **React 19** with TypeScript
- **Vite 7** for development and building
- **TanStack Query** for server state management
- **Zustand** for client state (per-entity stores)
- **Three.js** for 3D model viewing
- **Tailwind CSS 4** for styling
- **SignalR** for real-time extraction progress

## Project Structure

```
src/
├── api/           # API client functions (axios-based)
├── components/    # Reusable UI components
│   ├── ui/        # Base components (inputs, modals, etc.)
│   ├── display/   # Display components (RichText, badges, etc.)
│   ├── feedback/  # Feedback components (loading, errors)
│   └── layout/    # Layout components (Navigation)
├── features/      # Feature modules (one per entity type)
├── stores/        # Zustand stores (one per entity + global stores)
├── hooks/         # Custom React hooks
├── lib/           # Utilities and helpers
├── styles/        # Global CSS and Tailwind config
├── router.tsx     # React Router configuration
├── App.tsx        # Root component with layout
└── main.tsx       # Application entry point
```

## Key Components

| Component | Description |
|-----------|-------------|
| `ModelViewer` | Three.js-based 3D viewer with animation controls |
| `Navigation` | Sidebar with entity type navigation |
| `GlobalSearch` | Full-text search across all entities |
| `DetailContainer` | Right panel showing selected entity details |
| `RichText` | Renders game text with resolved placeholders |
| `ExtractionPanel` | Real-time extraction progress display |

## State Management

Each entity type has its own Zustand store (e.g., `unitsStore.ts`, `heroesStore.ts`) managing:
- Entity list and loading state
- Selected entity
- Filters and sorting
- Pagination

Global stores handle:
- `gameStore` - Game path, loading status, locale
- `imageStore` - Icon/texture caching
- `extractionStore` - Asset extraction progress
- `viewerStore` - 3D viewer state

## Development

```bash
# Install dependencies
npm install

# Start dev server (hot reload)
npm run dev

# Build and type check
npm run build

# Lint
npm run lint
```

The dev server runs at `http://localhost:5173` and proxies API requests to the backend at `http://localhost:5176`.

## Pages

Each page follows a consistent pattern:
1. List view with filterable/sortable table
2. Detail panel for selected item

| Page | Entity Type |
|------|-------------|
| UnitsPage | Game units with stats, abilities |
| HeroesPage | Hero specializations and skills |
| SpellsPage | Magic spells and effects |
| ArtifactsPage | Items and equipment |
| BuildingsPage | Town buildings |
| SkillsPage | Hero skills |
| AbilitiesPage | Unit/spell abilities |
| SubclassesPage | Hero subclasses |
| FactionLawsPage | Faction-specific laws |
| MapObjectsPage | Map objects |
| ViewerPage | Standalone 3D model viewer |
