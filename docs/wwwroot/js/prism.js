for (const code of document.querySelectorAll('pre > code')) {
    // Markdown attributes belong to <code>; Prism's line highlights belong to <pre>.
    if (code.dataset.line) {
        code.parentElement.dataset.line = code.dataset.line;
        code.removeAttribute('data-line');
    }

    // Prism has no "pwsh" alias for its PowerShell grammar.
    code.classList.replace('language-pwsh', 'language-powershell');
}
