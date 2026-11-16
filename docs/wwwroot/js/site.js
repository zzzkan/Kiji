// Copy buttons for code blocks.
if (navigator.clipboard) {
    for (const pre of document.querySelectorAll('pre')) {
        const block = document.createElement('div');
        block.className = 'code-block';
        pre.replaceWith(block);

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'copy-code';
        button.textContent = 'Copy';
        button.addEventListener('click', async () => {
            await navigator.clipboard.writeText(pre.querySelector('code').textContent);
            button.textContent = 'Copied';
            setTimeout(() => { button.textContent = 'Copy'; }, 2000);
        });
        block.append(pre, button);
    }
}

// Mark the section being read in "On this page".
const links = new Map();
for (const link of document.querySelectorAll('.outline a')) {
    const heading = document.getElementById(decodeURIComponent(link.hash.slice(1)));
    if (heading) {
        links.set(heading, link);
    }
}

if (links.size > 0) {
    const headings = [...links.keys()];
    let current;
    const update = () => {
        // The last heading above a line just below the sticky header; the final one at the page end.
        const atEnd = innerHeight + scrollY >= document.documentElement.scrollHeight - 2;
        const active = atEnd
            ? headings.at(-1)
            : headings.findLast(heading => heading.getBoundingClientRect().top < 120) ?? headings[0];
        if (active !== current) {
            links.get(current)?.removeAttribute('aria-current');
            links.get(active).setAttribute('aria-current', 'true');
            current = active;
        }
    };

    addEventListener('scroll', update, { passive: true });
    update();
}
