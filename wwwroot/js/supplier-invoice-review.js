export function returnToList() {
    const heading = document.getElementById("supplier-invoice-list-top");
    if (!heading) return;
    heading.focus({ preventScroll: true });
    heading.scrollIntoView({ behavior: "instant", block: "start" });
}
