import { lazy } from 'react';
import { createBrowserRouter, Navigate } from 'react-router-dom';
import App from './App';
import { ErrorPage } from '@/components/feedback';

const UnitsPage = lazy(() => import('./features/units/UnitsPage'));
const BuildingsPage = lazy(() => import('./features/buildings/BuildingsPage'));
const HeroesPage = lazy(() => import('./features/heroes/HeroesPage'));
const SpellsPage = lazy(() => import('./features/spells/SpellsPage'));
const SkillsPage = lazy(() => import('./features/skills/SkillsPage'));
const SubclassesPage = lazy(() => import('./features/subclasses/SubclassesPage'));
const FactionLawsPage = lazy(() => import('./features/faction-laws/FactionLawsPage'));
const MapObjectsPage = lazy(() => import('./features/map-objects/MapObjectsPage'));
const ArtifactsPage = lazy(() => import('./features/artifacts/ArtifactsPage'));
const AbilitiesPage = lazy(() => import('./features/abilities/AbilitiesPage'));
const ViewerPage = lazy(() => import('./features/viewer/ViewerPage'));

export const router = createBrowserRouter([
  {
    path: '/',
    element: <App />,
    errorElement: <ErrorPage />,
    children: [
      {
        index: true,
        element: <Navigate to="/units" replace />,
      },
      {
        path: 'units',
        element: <UnitsPage />,
      },
      {
        path: 'units/:unitId',
        element: <UnitsPage />,
      },
      {
        path: 'buildings',
        element: <BuildingsPage />,
      },
      {
        path: 'buildings/:buildingId',
        element: <BuildingsPage />,
      },
      {
        path: 'heroes',
        element: <HeroesPage />,
      },
      {
        path: 'heroes/:heroId',
        element: <HeroesPage />,
      },
      {
        path: 'spells',
        element: <SpellsPage />,
      },
      {
        path: 'spells/:spellId',
        element: <SpellsPage />,
      },
      {
        path: 'skills',
        element: <SkillsPage />,
      },
      {
        path: 'skills/:skillId',
        element: <SkillsPage />,
      },
      {
        path: 'subclasses',
        element: <SubclassesPage />,
      },
      {
        path: 'subclasses/:subclassId',
        element: <SubclassesPage />,
      },
      {
        path: 'faction-laws',
        element: <FactionLawsPage />,
      },
      {
        path: 'faction-laws/:factionLawId',
        element: <FactionLawsPage />,
      },
      {
        path: 'map-objects',
        element: <MapObjectsPage />,
      },
      {
        path: 'map-objects/*',
        element: <MapObjectsPage />,
      },
      {
        path: 'artifacts',
        element: <ArtifactsPage />,
      },
      {
        path: 'artifacts/:artifactId',
        element: <ArtifactsPage />,
      },
      {
        path: 'abilities',
        element: <AbilitiesPage />,
      },
      {
        path: 'abilities/:abilityId',
        element: <AbilitiesPage />,
      },
      {
        path: 'viewer',
        element: <ViewerPage />,
      },
      {
        path: 'viewer/units',
        element: <ViewerPage />,
      },
      {
        path: 'viewer/units/:modelId',
        element: <ViewerPage />,
      },
      {
        path: 'viewer/map-objects',
        element: <ViewerPage />,
      },
      {
        path: 'viewer/map-objects/*',
        element: <ViewerPage />,
      },
      {
        path: 'viewer/artifacts',
        element: <ViewerPage />,
      },
      {
        path: 'viewer/artifacts/*',
        element: <ViewerPage />,
      },
    ],
  },
]);
