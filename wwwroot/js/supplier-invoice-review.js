export function returnToList(targetId = "supplier-invoice-list-top") {
    const heading = document.getElementById(targetId);
    if (!heading) return;
    heading.focus({ preventScroll: true });
    heading.scrollIntoView({ behavior: "instant", block: "start" });
}
