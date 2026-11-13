// Saves text the page already holds as a file. Nothing is sent anywhere.
window.chisSaveFile = (name, mime, text) => {
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([text], { type: mime }));
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(link.href), 1000);
};
