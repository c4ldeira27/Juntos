/** Product name (not translated). Change it here and in index.html's <title>. */
export const APP_NAME = 'Juntos'

/**
 * Palette for the personal agenda (one color per calendar). In the group agenda the color
 * belongs to the PERSON and comes from the back end (GroupRules.MemberColors).
 */
export const PALETTE = [
  '#7c6cff', // violet
  '#ff7a6b', // coral
  '#14b8a6', // teal
  '#f5a524', // amber
  '#38bdf8', // sky
  '#ec4899', // pink
  '#84cc16', // lime
  '#a78bfa', // lavender
] as const

export const colorAt = (index: number) => PALETTE[index % PALETTE.length]
