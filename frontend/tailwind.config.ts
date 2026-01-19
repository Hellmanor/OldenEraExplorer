import type { Config } from 'tailwindcss'
import tailwindcssAnimate from 'tailwindcss-animate'

const config: Config = {
  darkMode: ['class'],
  content: [
    './index.html',
    './src/**/*.{ts,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        border: 'hsl(var(--border))',
        input: 'hsl(var(--input))',
        ring: 'hsl(var(--ring))',
        background: 'hsl(var(--background))',
        foreground: 'hsl(var(--foreground))',
        primary: {
          DEFAULT: 'hsl(var(--primary))',
          foreground: 'hsl(var(--primary-foreground))',
        },
        secondary: {
          DEFAULT: 'hsl(var(--secondary))',
          foreground: 'hsl(var(--secondary-foreground))',
        },
        destructive: {
          DEFAULT: 'hsl(var(--destructive))',
          foreground: 'hsl(var(--destructive-foreground))',
        },
        muted: {
          DEFAULT: 'hsl(var(--muted))',
          foreground: 'hsl(var(--muted-foreground))',
        },
        accent: {
          DEFAULT: 'hsl(var(--accent))',
          foreground: 'hsl(var(--accent-foreground))',
        },
        popover: {
          DEFAULT: 'hsl(var(--popover))',
          foreground: 'hsl(var(--popover-foreground))',
        },
        card: {
          DEFAULT: 'hsl(var(--card))',
          foreground: 'hsl(var(--card-foreground))',
        },
        // Semantic colors (WCAG compliant, theme-aware)
        semantic: {
          gold: 'hsl(var(--semantic-gold))',
          orange: 'hsl(var(--semantic-orange))',
          green: 'hsl(var(--semantic-green))',
          blue: 'hsl(var(--semantic-blue))',
          red: 'hsl(var(--semantic-red))',
          purple: 'hsl(var(--semantic-purple))',
        },
        // School colors (spell schools)
        school: {
          day: 'hsl(var(--school-day))',
          night: 'hsl(var(--school-night))',
          primal: 'hsl(var(--school-primal))',
          space: 'hsl(var(--school-space))',
          neutral: 'hsl(var(--school-neutral))',
        },
        // Rarity colors (artifacts)
        rarity: {
          common: 'hsl(var(--rarity-common))',
          uncommon: 'hsl(var(--rarity-uncommon))',
          rare: 'hsl(var(--rarity-rare))',
          epic: 'hsl(var(--rarity-epic))',
          legendary: 'hsl(var(--rarity-legendary))',
          mythic: 'hsl(var(--rarity-mythic))',
        },
      },
      borderRadius: {
        lg: 'var(--radius)',
        md: 'calc(var(--radius) - 2px)',
        sm: 'calc(var(--radius) - 4px)',
      },
    },
  },
  plugins: [tailwindcssAnimate],
}

export default config
