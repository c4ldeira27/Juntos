/** Nome provisório: troque aqui (e no <title> do index.html). */
export const APP_NAME = 'Juntos'

/**
 * Paleta das fontes. No M1 cada agenda tem uma cor; no M3 a cor passa a ser da PESSOA
 * e as agendas dela viram tons dessa cor.
 */
export const PALETTE = [
  '#7c6cff', // violeta
  '#ff7a6b', // coral
  '#14b8a6', // teal
  '#f5a524', // âmbar
  '#38bdf8', // céu
  '#ec4899', // rosa
  '#84cc16', // lima
  '#a78bfa', // lavanda
] as const

export const colorAt = (index: number) => PALETTE[index % PALETTE.length]
