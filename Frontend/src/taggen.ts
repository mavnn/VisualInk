export type Element = {
  tag: string,
  attributes?: Record<string, string>,
  className?: string,
  handlers?: Parameters<HTMLElement["addEventListener"]>[]
  children?: (Element | string)[]
}

const isElement = (input: string | Element): input is Element => {
  return (input as any).tag != undefined
}

export const buildDom = (element: Element): HTMLElement => {
  const domElement = document.createElement(element.tag)
  setAttributes(domElement, element.attributes ?? {})
  for(const handler of element.handlers ?? []) {
    domElement.addEventListener(...handler)
  }
  if (element.className) {
    domElement.className = element.className
  }
  domElement.append(...(element.children ?? []).map((child) => isElement(child) ? buildDom(child) : child))
  return domElement
}

const setAttributes = (element: HTMLElement, attributes: Record<string, string>) => {
  Object.entries(attributes).forEach(([name, value]) => element.setAttribute(name, value))
}
