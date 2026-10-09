import { themes as prismThemes } from 'prism-react-renderer';
import type { Config } from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const repository = 'https://github.com/MarcelRoozekrans/Minos.NET';

const config: Config = {
  title: 'Minos',
  tagline: 'Unofficial .NET client for decision models: send a state and typed questions, get calibrated, typed answers back',
  // A copy of assets/brand/favicon.ico, the 16/32/48 px bronze favicon.
  favicon: 'img/favicon.ico',

  // url and baseUrl decide the absolute links the built site emits; organizationName and projectName name the
  // repository it deploys from. All four have to agree, or the site builds clean and links off-site.
  // tests/Minos.NET.Docs.Tests/ReadmeLinkTests.cs checks the README's site links against the same address.
  url: 'https://marcelroozekrans.github.io',
  baseUrl: '/Minos.NET/',
  organizationName: 'MarcelRoozekrans',
  projectName: 'Minos.NET',
  trailingSlash: false,

  onBrokenLinks: 'throw',
  i18n: { defaultLocale: 'en', locales: ['en'] },

  markdown: { format: 'detect', mermaid: true },
  themes: ['@docusaurus/theme-mermaid'],

  presets: [
    [
      'classic',
      {
        docs: {
          path: 'docs',
          routeBasePath: '/',
          sidebarPath: './sidebars.ts',
          editUrl: `${repository}/edit/main/`,
          // The guide is docs/*.md and docs/patterns/. The rest of docs/ is the project's own records: the roadmap
          // and state (planning), design and implementation plans and review reports (plans, superpowers) and the
          // logo's construction record (design). They stay readable in the repository and are not published.
          exclude: [
            '**/README.md',
            '**/plans/**',
            '**/planning/**',
            '**/superpowers/**',
            '**/design/**',
          ],
        },
        blog: false,
        theme: { customCss: './src/css/custom.css' },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    navbar: {
      title: 'Minos',
      // A bronze-resolved copy of assets/brand/logo-mark.svg. The master paints in currentColor, which an <img> has
      // no colour for, so it would render black and vanish on the dark theme; #A86B24 reads on both.
      logo: { alt: 'Minos', src: 'img/logo.svg' },
      items: [
        { type: 'docSidebar', sidebarId: 'guideSidebar', position: 'left', label: 'Guide' },
        { href: repository, label: 'GitHub', position: 'right' },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Guide',
          items: [
            { label: 'Getting started', to: '/' },
            { label: 'Patterns', to: '/patterns' },
            { label: 'Diagnostics', to: '/diagnostics' },
          ],
        },
        {
          title: 'More',
          items: [
            { label: 'GitHub', href: repository },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Marcel Roozekrans. Minos is not affiliated with TypeSafe AI. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json', 'yaml', 'powershell'],
    },
    mermaid: {
      theme: { light: 'neutral', dark: 'dark' },
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
