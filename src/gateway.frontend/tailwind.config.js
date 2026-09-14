/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        display: ['"Space Grotesk"', 'sans-serif'],
        sans: ['"DM Sans"', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'monospace'],
      },
      colors: {
        ink: '#17221f',
        paper: '#f4f0e8',
        mint: '#cfe8d7',
        coral: '#e36d50',
        moss: '#5b735f',
      },
    },
  },
  plugins: [],
}