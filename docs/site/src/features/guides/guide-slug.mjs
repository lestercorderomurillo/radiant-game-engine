/** Old public URLs remain readable while navigation uses concept names. */
export const guideAliases = { hrc: "lighting", udr4: "upscaling" }

/** Shared by the client and static route generation. */
export const slugFromFile = (file) => {
  const slug = file
    .replace(/\.md$/, "")
    .replace(/([a-z])([A-Z])/g, "$1-$2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1-$2")
    .toLowerCase()
  return guideAliases[slug] ?? slug
}
