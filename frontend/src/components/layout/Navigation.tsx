import { NavLink } from 'react-router-dom';
import { cn } from '@/lib/utils';
import { useLabels } from '@/hooks/useLabels';

const Navigation = () => {
  const { label } = useLabels();

  const navLinks = [
    { path: '/units', text: label('nav_units') },
    { path: '/abilities', text: label('nav_abilities') },
    { path: '/heroes', text: label('nav_heroes') },
    { path: '/skills', text: label('nav_skills') },
    { path: '/subclasses', text: label('nav_subclasses') },
    { path: '/spells', text: label('nav_spells') },
    { path: '/artifacts', text: label('nav_artifacts') },
    { path: '/map-objects', text: label('nav_map_objects') },
    { path: '/buildings', text: label('nav_buildings') },
    { path: '/faction-laws', text: label('nav_faction_laws') },
    { path: '/viewer', text: label('nav_3d_viewer') },
  ];

  return (
    <nav className="bg-card border-b border-border px-6 flex gap-1 overflow-x-auto ml-80 lg:ml-105">
      {navLinks.map(({ path, text }) => (
        <NavLink
          key={path}
          to={path}
          className={({ isActive }) =>
            cn(
              'py-3 px-4 no-underline text-sm transition-all whitespace-nowrap inline-block',
              'border-b-2',
              isActive
                ? 'text-foreground border-primary font-medium'
                : 'text-muted-foreground border-transparent font-normal hover:text-foreground'
            )
          }
        >
          {text}
        </NavLink>
      ))}
    </nav>
  );
};

export default Navigation;
