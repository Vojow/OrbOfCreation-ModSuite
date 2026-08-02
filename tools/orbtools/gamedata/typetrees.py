from __future__ import annotations

import contextlib
import json
import logging
import os
import sys
from pathlib import Path
from typing import Any, Iterator


UNITY_VERSION = "6000.0.70f1"
MONO_HEADER = ("m_GameObject", "m_Enabled", "m_Script", "m_Name")


@contextlib.contextmanager
def silence_native_output(enabled: bool = True) -> Iterator[None]:
    if not enabled:
        yield
        return
    stdout_copy, stderr_copy = os.dup(1), os.dup(2)
    devnull = os.open(os.devnull, os.O_WRONLY)
    try:
        sys.stdout.flush()
        sys.stderr.flush()
        os.dup2(devnull, 1)
        os.dup2(devnull, 2)
        yield
    finally:
        os.dup2(stdout_copy, 1)
        os.dup2(stderr_copy, 2)
        os.close(stdout_copy)
        os.close(stderr_copy)
        os.close(devnull)


def strip_exact_monobehaviour_header(nodes: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Remove only the generator's exact four-field MonoBehaviour prefix.

    Managed-reference payloads do not serialize that prefix. A changed or partial
    prefix is left untouched so a generator drift cannot silently reshape data.
    """
    roots: list[tuple[int, int]] = []
    for index, node in enumerate(nodes):
        if int(node.get("m_Level", -1)) == 1:
            if roots:
                roots[-1] = (roots[-1][0], index)
            roots.append((index, len(nodes)))
    if len(roots) < len(MONO_HEADER):
        return nodes
    names = tuple(nodes[start].get("m_Name") for start, _end in roots[:4])
    if names != MONO_HEADER:
        return nodes
    remove_until = roots[3][1]
    return [nodes[0], *nodes[remove_until:]]


class TypeTreeSupport:
    def __init__(self, managed_directory: Path, *, debug: bool = False):
        from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

        self.debug = debug
        self.generator = TypeTreeGenerator(UNITY_VERSION)
        self.generator.load_local_dll_folder(str(managed_directory))
        self._top_cache: dict[tuple[str, str], list[dict[str, Any]] | None] = {}
        self._reference_cache: dict[tuple[str, str, str], Any] = {}

    @staticmethod
    def _assembly_name(value: str | None) -> str:
        return (value or "Assembly-CSharp").removesuffix(".dll")

    def nodes_for(self, assembly: str | None, class_name: str) -> list[dict[str, Any]] | None:
        key = (self._assembly_name(assembly), class_name)
        if key not in self._top_cache:
            with silence_native_output(not self.debug):
                try:
                    encoded = self.generator.get_nodes_as_json(*key)
                except Exception as error:
                    logging.getLogger(__name__).debug("Type tree failed for %s: %s", key, error)
                    encoded = None
            self._top_cache[key] = json.loads(encoded) if encoded else None
        return self._top_cache[key]

    def _reference_node(self, reference: dict[str, Any], _asset_file: Any):
        from UnityPy.helpers.TypeTreeNode import TypeTreeNode

        identity = reference["type"]
        if isinstance(identity, dict):
            class_name = identity.get("class")
            namespace = identity.get("ns") or ""
            assembly = identity.get("asm") or "Assembly-CSharp"
        else:
            class_name = getattr(identity, "class")
            namespace = getattr(identity, "ns", "") or ""
            assembly = getattr(identity, "asm", "Assembly-CSharp") or "Assembly-CSharp"
        if not class_name:
            return None
        key = (assembly, namespace, class_name)
        if key in self._reference_cache:
            return self._reference_cache[key]

        candidates = [f"{namespace}.{class_name}" if namespace else class_name, class_name]
        node = None
        for full_name in candidates:
            nodes = self.nodes_for(assembly, full_name)
            if not nodes:
                continue
            bounded = strip_exact_monobehaviour_header(nodes)
            node = TypeTreeNode.from_list(bounded)
            break
        if node is None:
            raise ValueError(
                f"Cannot generate managed-reference type tree: "
                f"class={class_name!r}, namespace={namespace!r}, assembly={assembly!r}"
            )
        self._reference_cache[key] = node
        return node

    def install_managed_reference_reader(self) -> None:
        import UnityPy.helpers.TypeTreeHelper as helper

        helper.read_typetree_boost = None
        helper.get_ref_type_node = self._reference_node
