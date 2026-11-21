for (const code of document.querySelectorAll('pre > code[data-line]')) {
    code.parentElement.dataset.line = code.dataset.line;
    code.removeAttribute('data-line');
}
