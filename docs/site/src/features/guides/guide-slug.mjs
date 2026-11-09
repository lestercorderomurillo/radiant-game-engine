/** Shared by the client and static route generation. */
export const slugFromFile = (file) => file
  .replace(/\.md$/, "")
  .replace(/([a-z])([A-Z])/g, "$1-$2")
  .replace(/([A-Z]+)([A-Z][a-z])/g, "$1-$2")
  .toLowerCase()
