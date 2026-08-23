export async function copyToClipboard(text) {
    if (navigator.clipboard && window.isSecureContext) {
        try {
            await navigator.clipboard.writeText(text);
            return;
        } catch {
            // A secure context isn't enough on its own - the write can still be refused by a
            // permissions policy or because the browser didn't credit the click as a user gesture.
            // Fall through to the textarea trick rather than giving up.
        }
    }

    // Fallback for insecure contexts (e.g. plain http on a LAN) where the async Clipboard API is unavailable.
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    try {
        if (!document.execCommand('copy')) {
            throw new Error('The browser refused to copy to the clipboard.');
        }
    } finally {
        document.body.removeChild(textarea);
    }
}
